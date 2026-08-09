using System.Text;

namespace Quillwright.Html;

/// <summary>
/// The tokenizer of the HTML standard (WHATWG HTML §13.2.5): the state machine that turns
/// characters into character runs, tags, comments, processing instructions and doctypes.
/// </summary>
/// <remarks>
/// <para>
/// Every state of the standard is here, including the six that untangle a script element's
/// escaped and double-escaped content, the RCDATA and RAWTEXT paths with their appropriate
/// end tag rule, the comment and doctype states with their recovery, CDATA, processing
/// instructions, and the character reference states with the legacy semicolon-less rule that
/// only applies outside attributes.
/// Parse errors are not reported — the standard requires a conforming parser to recover
/// exactly as described, which is what matters here, and it does.
/// </para>
/// <para>
/// The one departure from the letter of the standard is that consecutive character tokens are
/// emitted as one run rather than one token per code point. The tree builder handles a run
/// exactly as it would handle the characters one at a time, splitting it where an insertion
/// mode treats leading whitespace differently from what follows.
/// </para>
/// </remarks>
internal sealed class HtmlTokenizer
{
    private const char Replacement = '�';

    private readonly string _input;
    private readonly Func<bool> _canStartCdata;
    private readonly StringBuilder _text = new();
    private readonly Queue<HtmlToken> _output = new();
    private readonly StringBuilder _temporary = new();
    private readonly StringBuilder _attributeName = new();
    private readonly StringBuilder _attributeValue = new();
    private readonly HashSet<string> _attributeNames;
    private readonly StringBuilder _doctypePublicIdentifier = new();
    private readonly StringBuilder _doctypeSystemIdentifier = new();

    private int _position;
    private int _line = 1;
    private int _textLine = 1;
    private State _state = State.Data;
    private State _returnState = State.Data;
    private HtmlToken? _tag;
    private HtmlToken? _comment;
    private HtmlToken? _doctype;
    private HtmlToken? _processingInstruction;
    private string _lastStartTagName = string.Empty;
    private int _processingInstructionLine;
    private bool _hasAttribute;
    private bool _hasDoctypePublicIdentifier;
    private bool _hasDoctypeSystemIdentifier;
    private int _characterReferenceCode;

    internal HtmlTokenizer(string input, Func<bool> canStartCdata)
        : this(input, canStartCdata, StringComparer.Ordinal)
    {
    }

    /// <summary>Test seam for verifying the complexity of duplicate-attribute detection.</summary>
    internal HtmlTokenizer(
        string input,
        Func<bool> canStartCdata,
        IEqualityComparer<string> attributeNameComparer)
    {
        _input = Preprocess(input);
        _canStartCdata = canStartCdata;
        _attributeNames = new HashSet<string>(attributeNameComparer);
    }

    /// <summary>
    /// The states of §13.2.5, named as the standard names them. The numbering of the standard
    /// is kept in the order rather than in the values.
    /// </summary>
    private enum State
    {
        Data,
        Rcdata,
        Rawtext,
        ScriptData,
        Plaintext,
        TagOpen,
        EndTagOpen,
        TagName,
        RcdataLessThanSign,
        RcdataEndTagOpen,
        RcdataEndTagName,
        RawtextLessThanSign,
        RawtextEndTagOpen,
        RawtextEndTagName,
        ScriptDataLessThanSign,
        ScriptDataEndTagOpen,
        ScriptDataEndTagName,
        ScriptDataEscapeStart,
        ScriptDataEscapeStartDash,
        ScriptDataEscaped,
        ScriptDataEscapedDash,
        ScriptDataEscapedDashDash,
        ScriptDataEscapedLessThanSign,
        ScriptDataEscapedEndTagOpen,
        ScriptDataEscapedEndTagName,
        ScriptDataDoubleEscapeStart,
        ScriptDataDoubleEscaped,
        ScriptDataDoubleEscapedDash,
        ScriptDataDoubleEscapedDashDash,
        ScriptDataDoubleEscapedLessThanSign,
        ScriptDataDoubleEscapeEnd,
        BeforeAttributeName,
        AttributeName,
        AfterAttributeName,
        BeforeAttributeValue,
        AttributeValueDoubleQuoted,
        AttributeValueSingleQuoted,
        AttributeValueUnquoted,
        AfterAttributeValueQuoted,
        SelfClosingStartTag,
        BogusComment,
        MarkupDeclarationOpen,
        CommentStart,
        CommentStartDash,
        Comment,
        CommentLessThanSign,
        CommentLessThanSignBang,
        CommentLessThanSignBangDash,
        CommentLessThanSignBangDashDash,
        CommentEndDash,
        CommentEnd,
        CommentEndBang,
        Doctype,
        BeforeDoctypeName,
        DoctypeName,
        AfterDoctypeName,
        AfterDoctypePublicKeyword,
        BeforeDoctypePublicIdentifier,
        DoctypePublicIdentifierDoubleQuoted,
        DoctypePublicIdentifierSingleQuoted,
        AfterDoctypePublicIdentifier,
        BetweenDoctypePublicAndSystemIdentifiers,
        AfterDoctypeSystemKeyword,
        BeforeDoctypeSystemIdentifier,
        DoctypeSystemIdentifierDoubleQuoted,
        DoctypeSystemIdentifierSingleQuoted,
        AfterDoctypeSystemIdentifier,
        BogusDoctype,
        CdataSection,
        CdataSectionBracket,
        CdataSectionEnd,
        ProcessingInstructionOpen,
        ProcessingInstructionTarget,
        AfterProcessingInstructionTarget,
        ProcessingInstructionData,
        ProcessingInstructionQuestionable,
        CharacterReference,
        NamedCharacterReference,
        AmbiguousAmpersand,
        NumericCharacterReference,
        HexadecimalCharacterReferenceStart,
        HexadecimalCharacterReference,
        DecimalCharacterReference,
        NumericCharacterReferenceEnd,
    }

    /// <summary>
    /// Where the tokenizer goes when the tree builder says so: the standard lets the tree
    /// builder switch the tokenizer for the elements whose content is not markup.
    /// </summary>
    public void SwitchToRcdata() => _state = State.Rcdata;

    /// <inheritdoc cref="SwitchToRcdata"/>
    public void SwitchToRawtext() => _state = State.Rawtext;

    /// <inheritdoc cref="SwitchToRcdata"/>
    public void SwitchToScriptData() => _state = State.ScriptData;

    /// <inheritdoc cref="SwitchToRcdata"/>
    public void SwitchToPlaintext() => _state = State.Plaintext;

    /// <summary>The next token, or an end-of-file token once the input is spent.</summary>
    public HtmlToken Next()
    {
        while (_output.Count == 0)
            Step();

        return _output.Dequeue();
    }

    /// <summary>
    /// The input as the standard requires it: carriage returns folded into line feeds, alone
    /// or in a pair (§13.2.3.5).
    /// </summary>
    private static string Preprocess(string input)
    {
        if (input.IndexOf('\r') < 0)
            return input;

        var normalized = new StringBuilder(input.Length);
        for (int i = 0; i < input.Length; i++)
        {
            if (input[i] != '\r')
            {
                normalized.Append(input[i]);
                continue;
            }

            normalized.Append('\n');
            if (i + 1 < input.Length && input[i + 1] == '\n')
                i++;
        }

        return normalized.ToString();
    }

    private bool AtEnd => _position >= _input.Length;

    private char Current => _input[_position];

    private char Consume()
    {
        char c = _input[_position++];
        if (c == '\n')
            _line++;

        return c;
    }

    private void Reconsume()
    {
        _position--;
        if (_input[_position] == '\n')
            _line--;
    }

    private bool Matches(string value, bool caseInsensitive)
    {
        if (_position + value.Length > _input.Length)
            return false;

        ReadOnlySpan<char> candidate = _input.AsSpan(_position, value.Length);
        if (!caseInsensitive)
            return candidate.SequenceEqual(value);

        for (int i = 0; i < candidate.Length; i++)
        {
            if (AsciiLower(candidate[i]) != AsciiLower(value[i]))
                return false;
        }

        return true;
    }

    private void Advance(int count)
    {
        for (int i = 0; i < count; i++)
            Consume();
    }

    private void EmitCharacter(char c)
    {
        if (_text.Length == 0)
            _textLine = _line;

        _text.Append(c);
    }

    private void EmitCharacters(ReadOnlySpan<char> text)
    {
        foreach (char c in text)
            EmitCharacter(c);
    }

    private void FlushText()
    {
        if (_text.Length == 0)
            return;

        var token = new HtmlToken { Kind = HtmlTokenKind.Character, Line = _textLine };
        token.Data.Append(_text);
        _text.Clear();
        _output.Enqueue(token);
    }

    private void Emit(HtmlToken token)
    {
        FlushText();
        if (token.Kind == HtmlTokenKind.StartTag)
            _lastStartTagName = token.TagName;

        _output.Enqueue(token);
    }

    private void EmitEndOfFile()
    {
        FlushText();
        _output.Enqueue(new HtmlToken { Kind = HtmlTokenKind.EndOfFile, Line = _line });
    }

    /// <summary>Whether an end tag being built matches the start tag whose content this is.</summary>
    private bool IsAppropriateEndTag() =>
        _tag is not null && string.Equals(_tag.TagName, _lastStartTagName, StringComparison.Ordinal);

    private void StartTag(HtmlTokenKind kind)
    {
        _attributeNames.Clear();
        _tag = new HtmlToken { Kind = kind, Line = _line };
    }

    private void StartAttribute()
    {
        FinishAttribute();
        _attributeName.Clear();
        _attributeValue.Clear();
        _hasAttribute = true;
    }

    /// <summary>
    /// Closes the attribute being built. A name already on the tag wins, which is how the
    /// standard drops a duplicate rather than overwriting it.
    /// </summary>
    private void FinishAttribute()
    {
        if (!_hasAttribute)
            return;

        _hasAttribute = false;
        if (_attributeName.Length == 0 || _tag is null)
            return;

        string name = _attributeName.ToString();
        if (!_attributeNames.Add(name))
            return;

        _tag.Attributes.Add(new HtmlAttribute(name, _attributeValue.ToString()));
    }

    private void EmitTag()
    {
        FinishAttribute();
        if (_tag is { } tag)
        {
            Emit(tag);
            _tag = null;
            _attributeNames.Clear();
        }
    }

    private static bool IsWhitespace(char c) => c is '\t' or '\n' or '\f' or ' ';

    private static char AsciiLower(char c) => c is >= 'A' and <= 'Z' ? (char)(c + 0x20) : c;

    private void Step()
    {
        switch (_state)
        {
            case State.Data:
                DataState();
                break;
            case State.Rcdata:
                RcdataState();
                break;
            case State.Rawtext:
                TextOnlyState(State.RawtextLessThanSign);
                break;
            case State.ScriptData:
                TextOnlyState(State.ScriptDataLessThanSign);
                break;
            case State.Plaintext:
                PlaintextState();
                break;
            case State.TagOpen:
                TagOpenState();
                break;
            case State.EndTagOpen:
                EndTagOpenState();
                break;
            case State.TagName:
                TagNameState();
                break;
            case State.RcdataLessThanSign:
                LessThanSignState(State.Rcdata, State.RcdataEndTagOpen);
                break;
            case State.RcdataEndTagOpen:
                EndTagOpenInTextState(State.Rcdata, State.RcdataEndTagName);
                break;
            case State.RcdataEndTagName:
                EndTagNameInTextState(State.Rcdata);
                break;
            case State.RawtextLessThanSign:
                LessThanSignState(State.Rawtext, State.RawtextEndTagOpen);
                break;
            case State.RawtextEndTagOpen:
                EndTagOpenInTextState(State.Rawtext, State.RawtextEndTagName);
                break;
            case State.RawtextEndTagName:
                EndTagNameInTextState(State.Rawtext);
                break;
            case State.ScriptDataLessThanSign:
                ScriptDataLessThanSignState();
                break;
            case State.ScriptDataEndTagOpen:
                EndTagOpenInTextState(State.ScriptData, State.ScriptDataEndTagName);
                break;
            case State.ScriptDataEndTagName:
                EndTagNameInTextState(State.ScriptData);
                break;
            case State.ScriptDataEscapeStart:
                ScriptDataEscapeStartState();
                break;
            case State.ScriptDataEscapeStartDash:
                ScriptDataEscapeStartDashState();
                break;
            case State.ScriptDataEscaped:
                ScriptDataEscapedState();
                break;
            case State.ScriptDataEscapedDash:
                ScriptDataEscapedDashState();
                break;
            case State.ScriptDataEscapedDashDash:
                ScriptDataEscapedDashDashState();
                break;
            case State.ScriptDataEscapedLessThanSign:
                ScriptDataEscapedLessThanSignState();
                break;
            case State.ScriptDataEscapedEndTagOpen:
                EndTagOpenInTextState(State.ScriptDataEscaped, State.ScriptDataEscapedEndTagName);
                break;
            case State.ScriptDataEscapedEndTagName:
                EndTagNameInTextState(State.ScriptDataEscaped);
                break;
            case State.ScriptDataDoubleEscapeStart:
                ScriptDataDoubleEscapeStartState();
                break;
            case State.ScriptDataDoubleEscaped:
                ScriptDataDoubleEscapedState();
                break;
            case State.ScriptDataDoubleEscapedDash:
                ScriptDataDoubleEscapedDashState();
                break;
            case State.ScriptDataDoubleEscapedDashDash:
                ScriptDataDoubleEscapedDashDashState();
                break;
            case State.ScriptDataDoubleEscapedLessThanSign:
                ScriptDataDoubleEscapedLessThanSignState();
                break;
            case State.ScriptDataDoubleEscapeEnd:
                ScriptDataDoubleEscapeEndState();
                break;
            case State.BeforeAttributeName:
                BeforeAttributeNameState();
                break;
            case State.AttributeName:
                AttributeNameState();
                break;
            case State.AfterAttributeName:
                AfterAttributeNameState();
                break;
            case State.BeforeAttributeValue:
                BeforeAttributeValueState();
                break;
            case State.AttributeValueDoubleQuoted:
                AttributeValueQuotedState('"');
                break;
            case State.AttributeValueSingleQuoted:
                AttributeValueQuotedState('\'');
                break;
            case State.AttributeValueUnquoted:
                AttributeValueUnquotedState();
                break;
            case State.AfterAttributeValueQuoted:
                AfterAttributeValueQuotedState();
                break;
            case State.SelfClosingStartTag:
                SelfClosingStartTagState();
                break;
            case State.BogusComment:
                BogusCommentState();
                break;
            case State.MarkupDeclarationOpen:
                MarkupDeclarationOpenState();
                break;
            case State.CommentStart:
                CommentStartState();
                break;
            case State.CommentStartDash:
                CommentStartDashState();
                break;
            case State.Comment:
                CommentState();
                break;
            case State.CommentLessThanSign:
                CommentLessThanSignState();
                break;
            case State.CommentLessThanSignBang:
                CommentLessThanSignBangState();
                break;
            case State.CommentLessThanSignBangDash:
                CommentLessThanSignBangDashState();
                break;
            case State.CommentLessThanSignBangDashDash:
                CommentLessThanSignBangDashDashState();
                break;
            case State.CommentEndDash:
                CommentEndDashState();
                break;
            case State.CommentEnd:
                CommentEndState();
                break;
            case State.CommentEndBang:
                CommentEndBangState();
                break;
            case State.Doctype:
                DoctypeState();
                break;
            case State.BeforeDoctypeName:
                BeforeDoctypeNameState();
                break;
            case State.DoctypeName:
                DoctypeNameState();
                break;
            case State.AfterDoctypeName:
                AfterDoctypeNameState();
                break;
            case State.AfterDoctypePublicKeyword:
                AfterDoctypeKeywordState(publicIdentifier: true);
                break;
            case State.BeforeDoctypePublicIdentifier:
                BeforeDoctypeIdentifierState(publicIdentifier: true);
                break;
            case State.DoctypePublicIdentifierDoubleQuoted:
                DoctypeIdentifierState(publicIdentifier: true, quote: '"');
                break;
            case State.DoctypePublicIdentifierSingleQuoted:
                DoctypeIdentifierState(publicIdentifier: true, quote: '\'');
                break;
            case State.AfterDoctypePublicIdentifier:
                AfterDoctypePublicIdentifierState();
                break;
            case State.BetweenDoctypePublicAndSystemIdentifiers:
                BetweenDoctypeIdentifiersState();
                break;
            case State.AfterDoctypeSystemKeyword:
                AfterDoctypeKeywordState(publicIdentifier: false);
                break;
            case State.BeforeDoctypeSystemIdentifier:
                BeforeDoctypeIdentifierState(publicIdentifier: false);
                break;
            case State.DoctypeSystemIdentifierDoubleQuoted:
                DoctypeIdentifierState(publicIdentifier: false, quote: '"');
                break;
            case State.DoctypeSystemIdentifierSingleQuoted:
                DoctypeIdentifierState(publicIdentifier: false, quote: '\'');
                break;
            case State.AfterDoctypeSystemIdentifier:
                AfterDoctypeSystemIdentifierState();
                break;
            case State.BogusDoctype:
                BogusDoctypeState();
                break;
            case State.CdataSection:
                CdataSectionState();
                break;
            case State.CdataSectionBracket:
                CdataSectionBracketState();
                break;
            case State.CdataSectionEnd:
                CdataSectionEndState();
                break;
            case State.ProcessingInstructionOpen:
                ProcessingInstructionOpenState();
                break;
            case State.ProcessingInstructionTarget:
                ProcessingInstructionTargetState();
                break;
            case State.AfterProcessingInstructionTarget:
                AfterProcessingInstructionTargetState();
                break;
            case State.ProcessingInstructionData:
                ProcessingInstructionDataState();
                break;
            case State.ProcessingInstructionQuestionable:
                ProcessingInstructionQuestionableState();
                break;
            case State.CharacterReference:
                CharacterReferenceState();
                break;
            case State.NamedCharacterReference:
                NamedCharacterReferenceState();
                break;
            case State.AmbiguousAmpersand:
                AmbiguousAmpersandState();
                break;
            case State.NumericCharacterReference:
                NumericCharacterReferenceState();
                break;
            case State.HexadecimalCharacterReferenceStart:
                HexadecimalCharacterReferenceStartState();
                break;
            case State.HexadecimalCharacterReference:
                HexadecimalCharacterReferenceState();
                break;
            case State.DecimalCharacterReference:
                DecimalCharacterReferenceState();
                break;
            case State.NumericCharacterReferenceEnd:
                NumericCharacterReferenceEndState();
                break;
            default:
                EmitEndOfFile();
                break;
        }
    }

    private void DataState()
    {
        if (AtEnd)
        {
            EmitEndOfFile();
            return;
        }

        char c = Consume();
        switch (c)
        {
            case '&':
                _returnState = State.Data;
                _state = State.CharacterReference;
                break;
            case '<':
                _state = State.TagOpen;
                break;
            default:
                EmitCharacter(c);
                break;
        }
    }

    private void RcdataState()
    {
        if (AtEnd)
        {
            EmitEndOfFile();
            return;
        }

        char c = Consume();
        switch (c)
        {
            case '&':
                _returnState = State.Rcdata;
                _state = State.CharacterReference;
                break;
            case '<':
                _state = State.RcdataLessThanSign;
                break;
            case '\0':
                EmitCharacter(Replacement);
                break;
            default:
                EmitCharacter(c);
                break;
        }
    }

    /// <summary>The RAWTEXT and script data states, which differ only in where a `&lt;` leads.</summary>
    private void TextOnlyState(State lessThan)
    {
        if (AtEnd)
        {
            EmitEndOfFile();
            return;
        }

        char c = Consume();
        if (c == '<')
            _state = lessThan;
        else
            EmitCharacter(c == '\0' ? Replacement : c);
    }

    private void PlaintextState()
    {
        if (AtEnd)
        {
            EmitEndOfFile();
            return;
        }

        char c = Consume();
        EmitCharacter(c == '\0' ? Replacement : c);
    }

    private void TagOpenState()
    {
        if (AtEnd)
        {
            EmitCharacter('<');
            EmitEndOfFile();
            return;
        }

        char c = Consume();
        switch (c)
        {
            case '!':
                _state = State.MarkupDeclarationOpen;
                break;
            case '/':
                _state = State.EndTagOpen;
                break;
            case '?':
                _temporary.Clear();
                _processingInstructionLine = _line;
                _state = State.ProcessingInstructionOpen;
                break;
            default:
                if (char.IsAsciiLetter(c))
                {
                    StartTag(HtmlTokenKind.StartTag);
                    Reconsume();
                    _state = State.TagName;
                }
                else
                {
                    EmitCharacter('<');
                    Reconsume();
                    _state = State.Data;
                }

                break;
        }
    }

    private void EndTagOpenState()
    {
        if (AtEnd)
        {
            EmitCharacter('<');
            EmitCharacter('/');
            EmitEndOfFile();
            return;
        }

        char c = Consume();
        if (char.IsAsciiLetter(c))
        {
            StartTag(HtmlTokenKind.EndTag);
            Reconsume();
            _state = State.TagName;
        }
        else if (c == '>')
        {
            _state = State.Data;
        }
        else
        {
            _comment = new HtmlToken { Kind = HtmlTokenKind.Comment, Line = _line };
            Reconsume();
            _state = State.BogusComment;
        }
    }

    private void TagNameState()
    {
        if (AtEnd)
        {
            EmitEndOfFile();
            return;
        }

        char c = Consume();
        if (IsWhitespace(c))
        {
            _state = State.BeforeAttributeName;
        }
        else if (c == '/')
        {
            _state = State.SelfClosingStartTag;
        }
        else if (c == '>')
        {
            _state = State.Data;
            EmitTag();
        }
        else
        {
            _tag?.Name.Append(c == '\0' ? Replacement : AsciiLower(c));
        }
    }

    private void LessThanSignState(State textState, State endTagOpen)
    {
        if (!AtEnd && Current == '/')
        {
            Consume();
            _temporary.Clear();
            _state = endTagOpen;
            return;
        }

        EmitCharacter('<');
        _state = textState;
    }

    private void EndTagOpenInTextState(State textState, State endTagName)
    {
        if (!AtEnd && char.IsAsciiLetter(Current))
        {
            StartTag(HtmlTokenKind.EndTag);
            _state = endTagName;
            return;
        }

        EmitCharacter('<');
        EmitCharacter('/');
        _state = textState;
    }

    /// <summary>
    /// The end tag name states of RCDATA, RAWTEXT and script data: an end tag counts only
    /// when it names the element whose content this is, and otherwise the characters are text.
    /// </summary>
    private void EndTagNameInTextState(State textState)
    {
        if (!AtEnd)
        {
            char c = Consume();
            if (IsWhitespace(c) && IsAppropriateEndTag())
            {
                _state = State.BeforeAttributeName;
                return;
            }

            if (c == '/' && IsAppropriateEndTag())
            {
                _state = State.SelfClosingStartTag;
                return;
            }

            if (c == '>' && IsAppropriateEndTag())
            {
                _state = State.Data;
                EmitTag();
                return;
            }

            if (char.IsAsciiLetter(c))
            {
                _tag?.Name.Append(AsciiLower(c));
                _temporary.Append(c);
                return;
            }

            Reconsume();
        }

        EmitCharacter('<');
        EmitCharacter('/');
        EmitCharacters(_temporary.ToString());
        _tag = null;
        _state = textState;
    }

    private void ScriptDataLessThanSignState()
    {
        if (!AtEnd && Current == '/')
        {
            Consume();
            _temporary.Clear();
            _state = State.ScriptDataEndTagOpen;
            return;
        }

        if (!AtEnd && Current == '!')
        {
            Consume();
            EmitCharacter('<');
            EmitCharacter('!');
            _state = State.ScriptDataEscapeStart;
            return;
        }

        EmitCharacter('<');
        _state = State.ScriptData;
    }

    private void ScriptDataEscapeStartState()
    {
        if (!AtEnd && Current == '-')
        {
            Consume();
            EmitCharacter('-');
            _state = State.ScriptDataEscapeStartDash;
            return;
        }

        _state = State.ScriptData;
    }

    private void ScriptDataEscapeStartDashState()
    {
        if (!AtEnd && Current == '-')
        {
            Consume();
            EmitCharacter('-');
            _state = State.ScriptDataEscapedDashDash;
            return;
        }

        _state = State.ScriptData;
    }

    private void ScriptDataEscapedState()
    {
        if (AtEnd)
        {
            EmitEndOfFile();
            return;
        }

        char c = Consume();
        switch (c)
        {
            case '-':
                EmitCharacter('-');
                _state = State.ScriptDataEscapedDash;
                break;
            case '<':
                _state = State.ScriptDataEscapedLessThanSign;
                break;
            default:
                EmitCharacter(c == '\0' ? Replacement : c);
                break;
        }
    }

    private void ScriptDataEscapedDashState()
    {
        if (AtEnd)
        {
            EmitEndOfFile();
            return;
        }

        char c = Consume();
        switch (c)
        {
            case '-':
                EmitCharacter('-');
                _state = State.ScriptDataEscapedDashDash;
                break;
            case '<':
                _state = State.ScriptDataEscapedLessThanSign;
                break;
            default:
                EmitCharacter(c == '\0' ? Replacement : c);
                _state = State.ScriptDataEscaped;
                break;
        }
    }

    private void ScriptDataEscapedDashDashState()
    {
        if (AtEnd)
        {
            EmitEndOfFile();
            return;
        }

        char c = Consume();
        switch (c)
        {
            case '-':
                EmitCharacter('-');
                break;
            case '<':
                _state = State.ScriptDataEscapedLessThanSign;
                break;
            case '>':
                EmitCharacter('>');
                _state = State.ScriptData;
                break;
            default:
                EmitCharacter(c == '\0' ? Replacement : c);
                _state = State.ScriptDataEscaped;
                break;
        }
    }

    private void ScriptDataEscapedLessThanSignState()
    {
        if (!AtEnd && Current == '/')
        {
            Consume();
            _temporary.Clear();
            _state = State.ScriptDataEscapedEndTagOpen;
            return;
        }

        if (!AtEnd && char.IsAsciiLetter(Current))
        {
            _temporary.Clear();
            EmitCharacter('<');
            _state = State.ScriptDataDoubleEscapeStart;
            return;
        }

        EmitCharacter('<');
        _state = State.ScriptDataEscaped;
    }

    private void ScriptDataDoubleEscapeStartState()
    {
        if (AtEnd)
        {
            _state = State.ScriptDataEscaped;
            return;
        }

        char c = Consume();
        if (IsWhitespace(c) || c is '/' or '>')
        {
            EmitCharacter(c);
            _state = string.Equals(_temporary.ToString(), "script", StringComparison.Ordinal)
                ? State.ScriptDataDoubleEscaped
                : State.ScriptDataEscaped;
            return;
        }

        if (char.IsAsciiLetter(c))
        {
            _temporary.Append(AsciiLower(c));
            EmitCharacter(c);
            return;
        }

        Reconsume();
        _state = State.ScriptDataEscaped;
    }

    private void ScriptDataDoubleEscapedState()
    {
        if (AtEnd)
        {
            EmitEndOfFile();
            return;
        }

        char c = Consume();
        switch (c)
        {
            case '-':
                EmitCharacter('-');
                _state = State.ScriptDataDoubleEscapedDash;
                break;
            case '<':
                EmitCharacter('<');
                _state = State.ScriptDataDoubleEscapedLessThanSign;
                break;
            default:
                EmitCharacter(c == '\0' ? Replacement : c);
                break;
        }
    }

    private void ScriptDataDoubleEscapedDashState()
    {
        if (AtEnd)
        {
            EmitEndOfFile();
            return;
        }

        char c = Consume();
        switch (c)
        {
            case '-':
                EmitCharacter('-');
                _state = State.ScriptDataDoubleEscapedDashDash;
                break;
            case '<':
                EmitCharacter('<');
                _state = State.ScriptDataDoubleEscapedLessThanSign;
                break;
            default:
                EmitCharacter(c == '\0' ? Replacement : c);
                _state = State.ScriptDataDoubleEscaped;
                break;
        }
    }

    private void ScriptDataDoubleEscapedDashDashState()
    {
        if (AtEnd)
        {
            EmitEndOfFile();
            return;
        }

        char c = Consume();
        switch (c)
        {
            case '-':
                EmitCharacter('-');
                break;
            case '<':
                EmitCharacter('<');
                _state = State.ScriptDataDoubleEscapedLessThanSign;
                break;
            case '>':
                EmitCharacter('>');
                _state = State.ScriptData;
                break;
            default:
                EmitCharacter(c == '\0' ? Replacement : c);
                _state = State.ScriptDataDoubleEscaped;
                break;
        }
    }

    private void ScriptDataDoubleEscapedLessThanSignState()
    {
        if (!AtEnd && Current == '/')
        {
            Consume();
            _temporary.Clear();
            EmitCharacter('/');
            _state = State.ScriptDataDoubleEscapeEnd;
            return;
        }

        _state = State.ScriptDataDoubleEscaped;
    }

    private void ScriptDataDoubleEscapeEndState()
    {
        if (AtEnd)
        {
            _state = State.ScriptDataDoubleEscaped;
            return;
        }

        char c = Consume();
        if (IsWhitespace(c) || c is '/' or '>')
        {
            EmitCharacter(c);
            _state = string.Equals(_temporary.ToString(), "script", StringComparison.Ordinal)
                ? State.ScriptDataEscaped
                : State.ScriptDataDoubleEscaped;
            return;
        }

        if (char.IsAsciiLetter(c))
        {
            _temporary.Append(AsciiLower(c));
            EmitCharacter(c);
            return;
        }

        Reconsume();
        _state = State.ScriptDataDoubleEscaped;
    }

    private void BeforeAttributeNameState()
    {
        if (AtEnd)
        {
            _state = State.AfterAttributeName;
            return;
        }

        char c = Consume();
        if (IsWhitespace(c))
            return;

        if (c is '/' or '>')
        {
            Reconsume();
            _state = State.AfterAttributeName;
            return;
        }

        if (c == '=')
        {
            StartAttribute();
            _attributeName.Append(c);
            _state = State.AttributeName;
            return;
        }

        StartAttribute();
        Reconsume();
        _state = State.AttributeName;
    }

    private void AttributeNameState()
    {
        if (AtEnd)
        {
            _state = State.AfterAttributeName;
            return;
        }

        char c = Consume();
        if (IsWhitespace(c) || c is '/' or '>')
        {
            Reconsume();
            _state = State.AfterAttributeName;
            return;
        }

        if (c == '=')
        {
            _state = State.BeforeAttributeValue;
            return;
        }

        _attributeName.Append(c == '\0' ? Replacement : AsciiLower(c));
    }

    private void AfterAttributeNameState()
    {
        if (AtEnd)
        {
            EmitEndOfFile();
            return;
        }

        char c = Consume();
        if (IsWhitespace(c))
            return;

        switch (c)
        {
            case '/':
                _state = State.SelfClosingStartTag;
                break;
            case '=':
                _state = State.BeforeAttributeValue;
                break;
            case '>':
                _state = State.Data;
                EmitTag();
                break;
            default:
                StartAttribute();
                Reconsume();
                _state = State.AttributeName;
                break;
        }
    }

    private void BeforeAttributeValueState()
    {
        if (AtEnd)
        {
            _state = State.AttributeValueUnquoted;
            return;
        }

        char c = Consume();
        if (IsWhitespace(c))
            return;

        switch (c)
        {
            case '"':
                _state = State.AttributeValueDoubleQuoted;
                break;
            case '\'':
                _state = State.AttributeValueSingleQuoted;
                break;
            case '>':
                _state = State.Data;
                EmitTag();
                break;
            default:
                Reconsume();
                _state = State.AttributeValueUnquoted;
                break;
        }
    }

    private void AttributeValueQuotedState(char quote)
    {
        if (AtEnd)
        {
            EmitEndOfFile();
            return;
        }

        char c = Consume();
        if (c == quote)
        {
            _state = State.AfterAttributeValueQuoted;
            return;
        }

        if (c == '&')
        {
            _returnState = quote == '"' ? State.AttributeValueDoubleQuoted : State.AttributeValueSingleQuoted;
            _state = State.CharacterReference;
            return;
        }

        _attributeValue.Append(c == '\0' ? Replacement : c);
    }

    private void AttributeValueUnquotedState()
    {
        if (AtEnd)
        {
            EmitEndOfFile();
            return;
        }

        char c = Consume();
        if (IsWhitespace(c))
        {
            _state = State.BeforeAttributeName;
            return;
        }

        switch (c)
        {
            case '&':
                _returnState = State.AttributeValueUnquoted;
                _state = State.CharacterReference;
                break;
            case '>':
                _state = State.Data;
                EmitTag();
                break;
            default:
                _attributeValue.Append(c == '\0' ? Replacement : c);
                break;
        }
    }

    private void AfterAttributeValueQuotedState()
    {
        if (AtEnd)
        {
            EmitEndOfFile();
            return;
        }

        char c = Consume();
        if (IsWhitespace(c))
        {
            _state = State.BeforeAttributeName;
            return;
        }

        switch (c)
        {
            case '/':
                _state = State.SelfClosingStartTag;
                break;
            case '>':
                _state = State.Data;
                EmitTag();
                break;
            default:
                Reconsume();
                _state = State.BeforeAttributeName;
                break;
        }
    }

    private void SelfClosingStartTagState()
    {
        if (AtEnd)
        {
            EmitEndOfFile();
            return;
        }

        char c = Consume();
        if (c == '>')
        {
            if (_tag is { } tag)
                tag.SelfClosing = true;

            _state = State.Data;
            EmitTag();
            return;
        }

        Reconsume();
        _state = State.BeforeAttributeName;
    }

    private void BogusCommentState()
    {
        if (AtEnd)
        {
            EmitComment();
            EmitEndOfFile();
            return;
        }

        char c = Consume();
        if (c == '>')
        {
            _state = State.Data;
            EmitComment();
            return;
        }

        _comment?.Data.Append(c == '\0' ? Replacement : c);
    }

    private void EmitComment()
    {
        if (_comment is { } comment)
        {
            Emit(comment);
            _comment = null;
        }
    }

    private void MarkupDeclarationOpenState()
    {
        if (Matches("--", caseInsensitive: false))
        {
            Advance(2);
            _comment = new HtmlToken { Kind = HtmlTokenKind.Comment, Line = _line };
            _state = State.CommentStart;
            return;
        }

        if (Matches("DOCTYPE", caseInsensitive: true))
        {
            Advance(7);
            _state = State.Doctype;
            return;
        }

        if (Matches("[CDATA[", caseInsensitive: false))
        {
            Advance(7);
            if (_canStartCdata())
            {
                _state = State.CdataSection;
                return;
            }

            _comment = new HtmlToken { Kind = HtmlTokenKind.Comment, Line = _line };
            _comment.Data.Append("[CDATA[");
            _state = State.BogusComment;
            return;
        }

        _comment = new HtmlToken { Kind = HtmlTokenKind.Comment, Line = _line };
        _state = State.BogusComment;
    }

    private void CommentStartState()
    {
        if (AtEnd)
        {
            _state = State.Comment;
            return;
        }

        char c = Consume();
        switch (c)
        {
            case '-':
                _state = State.CommentStartDash;
                break;
            case '>':
                _state = State.Data;
                EmitComment();
                break;
            default:
                Reconsume();
                _state = State.Comment;
                break;
        }
    }

    private void CommentStartDashState()
    {
        if (AtEnd)
        {
            EmitComment();
            EmitEndOfFile();
            return;
        }

        char c = Consume();
        switch (c)
        {
            case '-':
                _state = State.CommentEnd;
                break;
            case '>':
                _state = State.Data;
                EmitComment();
                break;
            default:
                _comment?.Data.Append('-');
                Reconsume();
                _state = State.Comment;
                break;
        }
    }

    private void CommentState()
    {
        if (AtEnd)
        {
            EmitComment();
            EmitEndOfFile();
            return;
        }

        char c = Consume();
        switch (c)
        {
            case '<':
                _comment?.Data.Append('<');
                _state = State.CommentLessThanSign;
                break;
            case '-':
                _state = State.CommentEndDash;
                break;
            default:
                _comment?.Data.Append(c == '\0' ? Replacement : c);
                break;
        }
    }

    private void CommentLessThanSignState()
    {
        if (AtEnd)
        {
            _state = State.Comment;
            return;
        }

        char c = Consume();
        switch (c)
        {
            case '!':
                _comment?.Data.Append('!');
                _state = State.CommentLessThanSignBang;
                break;
            case '<':
                _comment?.Data.Append('<');
                break;
            default:
                Reconsume();
                _state = State.Comment;
                break;
        }
    }

    private void CommentLessThanSignBangState()
    {
        if (!AtEnd && Current == '-')
        {
            Consume();
            _state = State.CommentLessThanSignBangDash;
            return;
        }

        _state = State.Comment;
    }

    private void CommentLessThanSignBangDashState()
    {
        if (!AtEnd && Current == '-')
        {
            Consume();
            _state = State.CommentLessThanSignBangDashDash;
            return;
        }

        _state = State.CommentEndDash;
    }

    private void CommentLessThanSignBangDashDashState()
    {
        if (!AtEnd && Current is not '>')
        {
            _state = State.CommentEnd;
            return;
        }

        _state = State.CommentEnd;
    }

    private void CommentEndDashState()
    {
        if (AtEnd)
        {
            EmitComment();
            EmitEndOfFile();
            return;
        }

        char c = Consume();
        if (c == '-')
        {
            _state = State.CommentEnd;
            return;
        }

        _comment?.Data.Append('-');
        Reconsume();
        _state = State.Comment;
    }

    private void CommentEndState()
    {
        if (AtEnd)
        {
            EmitComment();
            EmitEndOfFile();
            return;
        }

        char c = Consume();
        switch (c)
        {
            case '>':
                _state = State.Data;
                EmitComment();
                break;
            case '!':
                _state = State.CommentEndBang;
                break;
            case '-':
                _comment?.Data.Append('-');
                break;
            default:
                _comment?.Data.Append("--");
                Reconsume();
                _state = State.Comment;
                break;
        }
    }

    private void CommentEndBangState()
    {
        if (AtEnd)
        {
            EmitComment();
            EmitEndOfFile();
            return;
        }

        char c = Consume();
        switch (c)
        {
            case '-':
                _comment?.Data.Append("--!");
                _state = State.CommentEndDash;
                break;
            case '>':
                _state = State.Data;
                EmitComment();
                break;
            default:
                _comment?.Data.Append("--!");
                Reconsume();
                _state = State.Comment;
                break;
        }
    }

    private void DoctypeState()
    {
        if (AtEnd)
        {
            StartDoctype(forceQuirks: true);
            EmitDoctype();
            EmitEndOfFile();
            return;
        }

        char c = Consume();
        if (IsWhitespace(c))
        {
            _state = State.BeforeDoctypeName;
            return;
        }

        if (c == '>')
        {
            Reconsume();
            _state = State.BeforeDoctypeName;
            return;
        }

        Reconsume();
        _state = State.BeforeDoctypeName;
    }

    private HtmlToken StartDoctype(bool forceQuirks = false)
    {
        _doctypePublicIdentifier.Clear();
        _doctypeSystemIdentifier.Clear();
        _hasDoctypePublicIdentifier = false;
        _hasDoctypeSystemIdentifier = false;

        var doctype = new HtmlToken
        {
            Kind = HtmlTokenKind.Doctype,
            Line = _line,
            ForceQuirks = forceQuirks,
        };
        _doctype = doctype;
        return doctype;
    }

    private void EmitDoctype()
    {
        if (_doctype is { } doctype)
        {
            doctype.PublicIdentifier = _hasDoctypePublicIdentifier
                ? _doctypePublicIdentifier.ToString()
                : null;
            doctype.SystemIdentifier = _hasDoctypeSystemIdentifier
                ? _doctypeSystemIdentifier.ToString()
                : null;
            Emit(doctype);
            _doctype = null;
        }
    }

    private void BeforeDoctypeNameState()
    {
        if (AtEnd)
        {
            StartDoctype(forceQuirks: true);
            EmitDoctype();
            EmitEndOfFile();
            return;
        }

        char c = Consume();
        if (IsWhitespace(c))
            return;

        HtmlToken doctype = StartDoctype();
        if (c == '>')
        {
            doctype.ForceQuirks = true;
            _state = State.Data;
            EmitDoctype();
            return;
        }

        doctype.Name.Append(c == '\0' ? Replacement : AsciiLower(c));
        _state = State.DoctypeName;
    }

    private void DoctypeNameState()
    {
        if (AtEnd)
        {
            if (_doctype is { } unterminated)
                unterminated.ForceQuirks = true;

            EmitDoctype();
            EmitEndOfFile();
            return;
        }

        char c = Consume();
        if (IsWhitespace(c))
        {
            _state = State.AfterDoctypeName;
            return;
        }

        if (c == '>')
        {
            _state = State.Data;
            EmitDoctype();
            return;
        }

        _doctype?.Name.Append(c == '\0' ? Replacement : AsciiLower(c));
    }

    private void AfterDoctypeNameState()
    {
        if (AtEnd)
        {
            if (_doctype is { } unterminated)
                unterminated.ForceQuirks = true;

            EmitDoctype();
            EmitEndOfFile();
            return;
        }

        if (IsWhitespace(Current))
        {
            Consume();
            return;
        }

        if (Current == '>')
        {
            Consume();
            _state = State.Data;
            EmitDoctype();
            return;
        }

        if (Matches("PUBLIC", caseInsensitive: true))
        {
            Advance(6);
            _state = State.AfterDoctypePublicKeyword;
            return;
        }

        if (Matches("SYSTEM", caseInsensitive: true))
        {
            Advance(6);
            _state = State.AfterDoctypeSystemKeyword;
            return;
        }

        if (_doctype is { } bogus)
            bogus.ForceQuirks = true;

        _state = State.BogusDoctype;
    }

    private void AfterDoctypeKeywordState(bool publicIdentifier)
    {
        if (AtEnd)
        {
            if (_doctype is { } unterminated)
                unterminated.ForceQuirks = true;

            EmitDoctype();
            EmitEndOfFile();
            return;
        }

        char c = Consume();
        if (IsWhitespace(c))
        {
            _state = publicIdentifier ? State.BeforeDoctypePublicIdentifier : State.BeforeDoctypeSystemIdentifier;
            return;
        }

        switch (c)
        {
            case '"':
                SetIdentifier(publicIdentifier, string.Empty);
                _state = publicIdentifier
                    ? State.DoctypePublicIdentifierDoubleQuoted
                    : State.DoctypeSystemIdentifierDoubleQuoted;
                break;
            case '\'':
                SetIdentifier(publicIdentifier, string.Empty);
                _state = publicIdentifier
                    ? State.DoctypePublicIdentifierSingleQuoted
                    : State.DoctypeSystemIdentifierSingleQuoted;
                break;
            case '>':
                if (_doctype is { } terminated)
                    terminated.ForceQuirks = true;

                _state = State.Data;
                EmitDoctype();
                break;
            default:
                if (_doctype is { } bogus)
                    bogus.ForceQuirks = true;

                Reconsume();
                _state = State.BogusDoctype;
                break;
        }
    }

    private void SetIdentifier(bool publicIdentifier, string value)
    {
        if (_doctype is null)
            return;

        if (publicIdentifier)
        {
            _hasDoctypePublicIdentifier = true;
            _doctypePublicIdentifier.Clear();
            _doctypePublicIdentifier.Append(value);
        }
        else
        {
            _hasDoctypeSystemIdentifier = true;
            _doctypeSystemIdentifier.Clear();
            _doctypeSystemIdentifier.Append(value);
        }
    }

    private void AppendIdentifier(bool publicIdentifier, char c)
    {
        if (_doctype is null)
            return;

        if (publicIdentifier)
        {
            _hasDoctypePublicIdentifier = true;
            _doctypePublicIdentifier.Append(c);
        }
        else
        {
            _hasDoctypeSystemIdentifier = true;
            _doctypeSystemIdentifier.Append(c);
        }
    }

    private void BeforeDoctypeIdentifierState(bool publicIdentifier)
    {
        if (AtEnd)
        {
            if (_doctype is { } unterminated)
                unterminated.ForceQuirks = true;

            EmitDoctype();
            EmitEndOfFile();
            return;
        }

        char c = Consume();
        if (IsWhitespace(c))
            return;

        if (c is '"' or '\'')
        {
            SetIdentifier(publicIdentifier, string.Empty);
            _state = c == '"'
                ? publicIdentifier ? State.DoctypePublicIdentifierDoubleQuoted : State.DoctypeSystemIdentifierDoubleQuoted
                : publicIdentifier ? State.DoctypePublicIdentifierSingleQuoted : State.DoctypeSystemIdentifierSingleQuoted;
            return;
        }

        if (_doctype is { } doctype)
            doctype.ForceQuirks = true;

        if (c == '>')
        {
            _state = State.Data;
            EmitDoctype();
            return;
        }

        Reconsume();
        _state = State.BogusDoctype;
    }

    private void DoctypeIdentifierState(bool publicIdentifier, char quote)
    {
        if (AtEnd)
        {
            if (_doctype is { } unterminated)
                unterminated.ForceQuirks = true;

            EmitDoctype();
            EmitEndOfFile();
            return;
        }

        char c = Consume();
        if (c == quote)
        {
            _state = publicIdentifier ? State.AfterDoctypePublicIdentifier : State.AfterDoctypeSystemIdentifier;
            return;
        }

        if (c == '>')
        {
            if (_doctype is { } doctype)
                doctype.ForceQuirks = true;

            _state = State.Data;
            EmitDoctype();
            return;
        }

        AppendIdentifier(publicIdentifier, c == '\0' ? Replacement : c);
    }

    private void AfterDoctypePublicIdentifierState()
    {
        if (AtEnd)
        {
            if (_doctype is { } unterminated)
                unterminated.ForceQuirks = true;

            EmitDoctype();
            EmitEndOfFile();
            return;
        }

        char c = Consume();
        if (IsWhitespace(c))
        {
            _state = State.BetweenDoctypePublicAndSystemIdentifiers;
            return;
        }

        switch (c)
        {
            case '>':
                _state = State.Data;
                EmitDoctype();
                break;
            case '"':
                SetIdentifier(publicIdentifier: false, string.Empty);
                _state = State.DoctypeSystemIdentifierDoubleQuoted;
                break;
            case '\'':
                SetIdentifier(publicIdentifier: false, string.Empty);
                _state = State.DoctypeSystemIdentifierSingleQuoted;
                break;
            default:
                if (_doctype is { } bogus)
                    bogus.ForceQuirks = true;

                Reconsume();
                _state = State.BogusDoctype;
                break;
        }
    }

    private void BetweenDoctypeIdentifiersState()
    {
        if (AtEnd)
        {
            if (_doctype is { } unterminated)
                unterminated.ForceQuirks = true;

            EmitDoctype();
            EmitEndOfFile();
            return;
        }

        char c = Consume();
        if (IsWhitespace(c))
            return;

        switch (c)
        {
            case '>':
                _state = State.Data;
                EmitDoctype();
                break;
            case '"':
                SetIdentifier(publicIdentifier: false, string.Empty);
                _state = State.DoctypeSystemIdentifierDoubleQuoted;
                break;
            case '\'':
                SetIdentifier(publicIdentifier: false, string.Empty);
                _state = State.DoctypeSystemIdentifierSingleQuoted;
                break;
            default:
                if (_doctype is { } bogus)
                    bogus.ForceQuirks = true;

                Reconsume();
                _state = State.BogusDoctype;
                break;
        }
    }

    private void AfterDoctypeSystemIdentifierState()
    {
        if (AtEnd)
        {
            if (_doctype is { } unterminated)
                unterminated.ForceQuirks = true;

            EmitDoctype();
            EmitEndOfFile();
            return;
        }

        char c = Consume();
        if (IsWhitespace(c))
            return;

        if (c == '>')
        {
            _state = State.Data;
            EmitDoctype();
            return;
        }

        Reconsume();
        _state = State.BogusDoctype;
    }

    private void BogusDoctypeState()
    {
        if (AtEnd)
        {
            EmitDoctype();
            EmitEndOfFile();
            return;
        }

        char c = Consume();
        if (c == '>')
        {
            _state = State.Data;
            EmitDoctype();
        }
    }

    private void CdataSectionState()
    {
        if (AtEnd)
        {
            EmitEndOfFile();
            return;
        }

        char c = Consume();
        if (c == ']')
        {
            _state = State.CdataSectionBracket;
            return;
        }

        EmitCharacter(c);
    }

    private void CdataSectionBracketState()
    {
        if (AtEnd)
        {
            EmitCharacter(']');
            EmitEndOfFile();
            return;
        }

        if (Current == ']')
        {
            Consume();
            _state = State.CdataSectionEnd;
            return;
        }

        EmitCharacter(']');
        _state = State.CdataSection;
    }

    private void CdataSectionEndState()
    {
        if (AtEnd)
        {
            EmitCharacters("]]");
            EmitEndOfFile();
            return;
        }

        char c = Consume();
        switch (c)
        {
            case ']':
                EmitCharacter(']');
                break;
            case '>':
                _state = State.Data;
                break;
            default:
                EmitCharacters("]]");
                Reconsume();
                _state = State.CdataSection;
                break;
        }
    }

    private void ProcessingInstructionOpenState()
    {
        if (AtEnd)
        {
            EmitEndOfFile();
            return;
        }

        char c = Consume();
        if (char.IsAsciiLetter(c) || c == '_')
        {
            Reconsume();
            _state = State.ProcessingInstructionTarget;
            return;
        }

        ConvertTemporaryBufferToComment();
        Reconsume();
        _state = State.BogusComment;
    }

    private void ProcessingInstructionTargetState()
    {
        if (AtEnd)
        {
            EmitEndOfFile();
            return;
        }

        char c = Consume();
        if (IsWhitespace(c) || c is '?' or '>')
        {
            string target = _temporary.ToString();
            if (target.Equals("xml", StringComparison.OrdinalIgnoreCase) ||
                target.Equals("xml-stylesheet", StringComparison.OrdinalIgnoreCase))
            {
                ConvertTemporaryBufferToComment();
                Reconsume();
                _state = State.BogusComment;
                return;
            }

            _processingInstruction = new HtmlToken
            {
                Kind = HtmlTokenKind.ProcessingInstruction,
                Line = _processingInstructionLine,
            };
            _processingInstruction.Name.Append(_temporary);
            Reconsume();
            _state = State.AfterProcessingInstructionTarget;
            return;
        }

        if (char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
        {
            _temporary.Append(c);
            return;
        }

        ConvertTemporaryBufferToComment();
        Reconsume();
        _state = State.BogusComment;
    }

    private void AfterProcessingInstructionTargetState()
    {
        if (AtEnd)
        {
            _processingInstruction = null;
            EmitEndOfFile();
            return;
        }

        char c = Consume();
        if (IsWhitespace(c))
            return;

        Reconsume();
        _state = State.ProcessingInstructionData;
    }

    private void ProcessingInstructionDataState()
    {
        if (AtEnd)
        {
            _processingInstruction = null;
            EmitEndOfFile();
            return;
        }

        char c = Consume();
        switch (c)
        {
            case '?':
                _state = State.ProcessingInstructionQuestionable;
                break;
            case '>':
                _state = State.Data;
                EmitProcessingInstruction();
                break;
            default:
                _processingInstruction?.Data.Append(c);
                break;
        }
    }

    private void ProcessingInstructionQuestionableState()
    {
        if (AtEnd)
        {
            _processingInstruction = null;
            EmitEndOfFile();
            return;
        }

        char c = Consume();
        if (c == '>')
        {
            _state = State.Data;
            EmitProcessingInstruction();
            return;
        }

        _processingInstruction?.Data.Append('?');
        Reconsume();
        _state = State.ProcessingInstructionData;
    }

    private void ConvertTemporaryBufferToComment()
    {
        _comment = new HtmlToken { Kind = HtmlTokenKind.Comment, Line = _processingInstructionLine };
        _comment.Data.Append('?').Append(_temporary);
    }

    private void EmitProcessingInstruction()
    {
        if (_processingInstruction is not { } instruction)
            return;

        Emit(instruction);
        _processingInstruction = null;
    }

    /// <summary>Whether the reference being read is inside an attribute value.</summary>
    private bool InAttribute => _returnState
        is State.AttributeValueDoubleQuoted
        or State.AttributeValueSingleQuoted
        or State.AttributeValueUnquoted;

    /// <summary>
    /// Puts the characters consumed as a reference where they belong: into the attribute
    /// value being built, or into the character run.
    /// </summary>
    private void FlushCodePoints()
    {
        if (InAttribute)
            _attributeValue.Append(_temporary);
        else
            EmitCharacters(_temporary.ToString());

        _temporary.Clear();
    }

    private void CharacterReferenceState()
    {
        _temporary.Clear();
        _temporary.Append('&');

        if (AtEnd)
        {
            FlushCodePoints();
            _state = _returnState;
            return;
        }

        char c = Current;
        if (char.IsAsciiLetterOrDigit(c))
        {
            _state = State.NamedCharacterReference;
            return;
        }

        if (c == '#')
        {
            Consume();
            _temporary.Append(c);
            _state = State.NumericCharacterReference;
            return;
        }

        FlushCodePoints();
        _state = _returnState;
    }

    /// <summary>
    /// Matches the longest name in the table, then applies the legacy rule: a name with no
    /// semicolon does not expand inside an attribute when what follows could make it part of
    /// something else (§13.2.5.78).
    /// </summary>
    private void NamedCharacterReferenceState()
    {
        int available = Math.Min(HtmlNamedCharacterReferences.LongestName, _input.Length - _position);
        int matched = 0;
        string? expansion = null;

        for (int length = available; length >= 1; length--)
        {
            if (HtmlNamedCharacterReferences.Lookup(_input.AsSpan(_position, length)) is { } found)
            {
                matched = length;
                expansion = found;
                break;
            }
        }

        if (expansion is null)
        {
            // Nothing matched: the characters stay as they are and an ambiguous ampersand
            // consumes what follows.
            FlushCodePoints();
            _state = State.AmbiguousAmpersand;
            return;
        }

        bool endsWithSemicolon = _input[_position + matched - 1] == ';';
        if (InAttribute && !endsWithSemicolon)
        {
            char next = _position + matched < _input.Length ? _input[_position + matched] : '\0';
            if (next == '=' || char.IsAsciiLetterOrDigit(next))
            {
                _temporary.Append(_input.AsSpan(_position, matched));
                Advance(matched);
                FlushCodePoints();
                _state = _returnState;
                return;
            }
        }

        Advance(matched);
        _temporary.Clear();
        _temporary.Append(expansion);
        FlushCodePoints();
        _state = _returnState;
    }

    private void AmbiguousAmpersandState()
    {
        if (AtEnd)
        {
            _state = _returnState;
            return;
        }

        char c = Consume();
        if (char.IsAsciiLetterOrDigit(c))
        {
            if (InAttribute)
                _attributeValue.Append(c);
            else
                EmitCharacter(c);

            return;
        }

        Reconsume();
        _state = _returnState;
    }

    private void NumericCharacterReferenceState()
    {
        _characterReferenceCode = 0;

        if (AtEnd)
        {
            FlushCodePoints();
            _state = _returnState;
            return;
        }

        char c = Current;
        if (c is 'x' or 'X')
        {
            Consume();
            _temporary.Append(c);
            _state = State.HexadecimalCharacterReferenceStart;
            return;
        }

        if (char.IsAsciiDigit(c))
        {
            _state = State.DecimalCharacterReference;
            return;
        }

        FlushCodePoints();
        _state = _returnState;
    }

    private void HexadecimalCharacterReferenceStartState()
    {
        if (!AtEnd && char.IsAsciiHexDigit(Current))
        {
            _state = State.HexadecimalCharacterReference;
            return;
        }

        FlushCodePoints();
        _state = _returnState;
    }

    private void HexadecimalCharacterReferenceState()
    {
        if (AtEnd)
        {
            _state = State.NumericCharacterReferenceEnd;
            return;
        }

        char c = Consume();
        if (char.IsAsciiDigit(c))
        {
            Accumulate(16, c - '0');
            return;
        }

        if (char.IsAsciiHexDigitUpper(c))
        {
            Accumulate(16, c - 'A' + 10);
            return;
        }

        if (char.IsAsciiHexDigitLower(c))
        {
            Accumulate(16, c - 'a' + 10);
            return;
        }

        if (c != ';')
            Reconsume();

        _state = State.NumericCharacterReferenceEnd;
    }

    private void DecimalCharacterReferenceState()
    {
        if (AtEnd)
        {
            _state = State.NumericCharacterReferenceEnd;
            return;
        }

        char c = Consume();
        if (char.IsAsciiDigit(c))
        {
            Accumulate(10, c - '0');
            return;
        }

        if (c != ';')
            Reconsume();

        _state = State.NumericCharacterReferenceEnd;
    }

    /// <summary>Grows the reference code, saturating rather than overflowing on a long run of digits.</summary>
    private void Accumulate(int radix, int digit)
    {
        if (_characterReferenceCode <= 0x10FFFF)
            _characterReferenceCode = (_characterReferenceCode * radix) + digit;
    }

    private void NumericCharacterReferenceEndState()
    {
        int code = _characterReferenceCode;
        if (code == 0 || code > 0x10FFFF || (code >= 0xD800 && code <= 0xDFFF))
            code = Replacement;
        else if (LegacyReplacement(code) is { } replacement)
            code = replacement;

        _temporary.Clear();
        _temporary.Append(char.ConvertFromUtf32(code));
        FlushCodePoints();
        _state = _returnState;
    }

    /// <summary>
    /// The numbers the standard remaps because authors meant the Windows-1252 character
    /// rather than the C1 control (§13.2.5.84).
    /// </summary>
    private static int? LegacyReplacement(int code) => code switch
    {
        0x80 => 0x20AC,
        0x82 => 0x201A,
        0x83 => 0x0192,
        0x84 => 0x201E,
        0x85 => 0x2026,
        0x86 => 0x2020,
        0x87 => 0x2021,
        0x88 => 0x02C6,
        0x89 => 0x2030,
        0x8A => 0x0160,
        0x8B => 0x2039,
        0x8C => 0x0152,
        0x8E => 0x017D,
        0x91 => 0x2018,
        0x92 => 0x2019,
        0x93 => 0x201C,
        0x94 => 0x201D,
        0x95 => 0x2022,
        0x96 => 0x2013,
        0x97 => 0x2014,
        0x98 => 0x02DC,
        0x99 => 0x2122,
        0x9A => 0x0161,
        0x9B => 0x203A,
        0x9C => 0x0153,
        0x9E => 0x017E,
        0x9F => 0x0178,
        _ => null,
    };
}
