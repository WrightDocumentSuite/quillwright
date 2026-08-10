using System.Buffers;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Quillwright.Diagnostics;
using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Rtf.Parsing;

internal sealed class RtfParser
{
    private readonly RtfImportOptions _options;
    private readonly CancellationToken _cancellationToken;
    private readonly DocumentLoadBudgetState _loadBudget;
    private readonly RtfImportDiagnostics _diagnostics = new();
    private readonly ArrayBufferWriter<byte> _encodedText = new();
    private readonly Dictionary<int, RtfFont> _fonts = [];
    private readonly Dictionary<int, StringBuilder> _fontNames = [];
    private readonly List<WordColor> _colors = [];
    private readonly Dictionary<string, RtfAnnotationPoint> _annotationStarts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RtfAnnotationPoint> _annotationEnds = new(StringComparer.Ordinal);
    private readonly List<RtfImportedAnnotation> _annotations = [];
    private readonly StringBuilder _destinationText = new();
    private WordDocument _document = null!;
    private Section _section = null!;
    private Paragraph _paragraph = null!;
    private int _textCharacters;
    private int _pendingCodePage = 1252;
    private int _defaultFontIndex;
    private int? _colorRed;
    private int? _colorGreen;
    private int? _colorBlue;
    private string? _pendingAnnotationId;
    private string? _pendingAnnotationAuthor;
    private RtfAnnotationTime? _activeAnnotationTime;
    private RtfAnnotationTime? _pendingAnnotationTime;
    private RtfAnnotationBuilder? _activeAnnotation;

    static RtfParser() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public RtfParser(RtfImportOptions options, CancellationToken cancellationToken = default)
    {
        _options = options;
        _cancellationToken = cancellationToken;
        _loadBudget = new DocumentLoadBudgetState(options.Budget);
    }

    public RtfImportResult Parse(ReadOnlySpan<byte> input)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        _options.Validate();
        if (input.Length > _options.MaxInputBytes)
            throw new RtfFormatException($"The input exceeds the {_options.MaxInputBytes}-byte limit", 0);

        _document = WordDocument.Create();
        _section = _document.Sections[0];
        _paragraph = new Paragraph();

        var tokenizer = new RtfTokenizer(input, _cancellationToken);
        var savedStates = new RtfState[_options.MaxGroupDepth];
        var frames = new RtfGroupFrame[_options.MaxGroupDepth + 1];
        var state = RtfState.Default;
        int depth = 0;
        bool sawRoot = false;
        bool sawRtf = false;
        bool rootClosed = false;

        while (tokenizer.TryRead(out RtfToken token))
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (rootClosed)
                throw new RtfFormatException("Content follows the root RTF group", token.Offset);

            switch (token.Kind)
            {
                case RtfTokenKind.GroupStart:
                    FlushEncodedText(state);
                    state.FallbackCharactersToSkip = 0;
                    if (!sawRoot)
                    {
                        sawRoot = true;
                    }
                    else if (depth == 0)
                    {
                        throw new RtfFormatException("An RTF file can contain only one root group", token.Offset);
                    }

                    if (depth >= _options.MaxGroupDepth)
                        throw new RtfFormatException($"RTF group nesting exceeds the {_options.MaxGroupDepth}-group limit", token.Offset);

                    bool unicodeAlternative = false;
                    bool skip = state.Skip;
                    if (depth > 0 && frames[depth].IsUnicodePair)
                    {
                        int child = ++frames[depth].ChildGroups;
                        skip = child == 1;
                        unicodeAlternative = child == 2;
                    }

                    savedStates[depth] = state;
                    depth++;
                    frames[depth] = new RtfGroupFrame
                    {
                        IsUnicodeAlternative = unicodeAlternative,
                        StartParagraph = _paragraph,
                        StartOffset = _paragraph.TextLength,
                    };
                    state.Skip = skip;
                    state.AtGroupStart = true;
                    state.StarDestination = false;
                    break;

                case RtfTokenKind.GroupEnd:
                    FlushEncodedText(state);
                    if (depth == 0)
                        throw new RtfFormatException("A closing brace has no matching opening brace", token.Offset);

                    CompleteDestination(frames[depth], state, token.Offset);
                    if (depth == 1)
                        CommitParagraph(force: false, state);

                    frames[depth] = default;
                    state = savedStates[depth - 1];
                    state.FallbackCharactersToSkip = 0;
                    depth--;
                    if (depth == 0)
                        rootClosed = true;
                    break;

                case RtfTokenKind.Text:
                    if (depth == 0)
                        throw new RtfFormatException("Document text appears outside the root group", token.Offset);
                    AppendEncoded(tokenizer.Slice(token), ref state);
                    state.AtGroupStart = false;
                    break;

                case RtfTokenKind.HexByte:
                    AppendHexByte((byte)token.Parameter, ref state);
                    state.AtGroupStart = false;
                    break;

                case RtfTokenKind.ControlSymbol:
                    FlushEncodedText(state);
                    ApplySymbol(token, ref state);
                    break;

                case RtfTokenKind.ControlWord:
                    FlushEncodedText(state);
                    ApplyControl(token, ref state, frames, depth, ref sawRtf);
                    break;

                case RtfTokenKind.Binary:
                    FlushEncodedText(state);
                    if (state.FallbackCharactersToSkip > 0)
                        state.FallbackCharactersToSkip--;
                    state.AtGroupStart = false;
                    break;
            }
        }

        FlushEncodedText(state);
        if (!sawRoot || !sawRtf)
            throw new RtfFormatException("The input is not an RTF document beginning with {\\rtf1", 0);
        if (depth != 0)
            throw new RtfFormatException("The root RTF group is not closed", input.Length);

        CommitParagraph(force: false, state);
        ImportAnnotations();
        return new RtfImportResult(_document, _diagnostics);
    }

    private void ApplyControl(
        RtfToken token,
        ref RtfState state,
        RtfGroupFrame[] frames,
        int depth,
        ref bool sawRtf)
    {
        bool atGroupStart = state.AtGroupStart;
        if (state.Skip && token.Keyword != RtfKeyword.UnicodeDestination)
        {
            state.AtGroupStart = false;
            return;
        }

        switch (token.Keyword)
        {
            case RtfKeyword.Rtf:
                if (depth != 1 || !atGroupStart || !token.HasParameter || token.Parameter != 1)
                    throw new RtfFormatException("The root group must begin with \\rtf1", token.Offset);
                sawRtf = true;
                break;
            case RtfKeyword.Ansi:
                state.CodePage = 1252;
                break;
            case RtfKeyword.Mac:
                state.CodePage = 10000;
                break;
            case RtfKeyword.Pc:
                state.CodePage = 437;
                break;
            case RtfKeyword.Pca:
                state.CodePage = 850;
                break;
            case RtfKeyword.AnsiCodePage when token.HasParameter && token.Parameter > 0:
                state.CodePage = token.Parameter;
                break;
            case RtfKeyword.CodePage when token.HasParameter && token.Parameter > 0:
                if (state.Destination == RtfDestination.FontTable)
                    state.FontCodePage = token.Parameter;
                else
                    state.CodePage = token.Parameter;
                break;
            case RtfKeyword.UnicodeSkipCount when token.HasParameter:
                state.UnicodeSkipCount = Math.Clamp(token.Parameter, 0, 255);
                break;
            case RtfKeyword.UnicodeCharacter when token.HasParameter:
                AppendCharacter((char)(ushort)token.Parameter, state);
                state.FallbackCharactersToSkip = state.UnicodeSkipCount;
                break;
            case RtfKeyword.Upr:
                if (depth > 0)
                    frames[depth].IsUnicodePair = true;
                break;
            case RtfKeyword.UnicodeDestination:
                if (depth > 0 && frames[depth].IsUnicodeAlternative)
                {
                    state.Skip = false;
                    state.StarDestination = false;
                }
                break;
            case RtfKeyword.FontTable:
                if (atGroupStart)
                {
                    state.Destination = RtfDestination.FontTable;
                    state.FontIndex = -1;
                    state.FontCharset = 0;
                    state.FontCodePage = 0;
                }
                break;
            case RtfKeyword.ColorTable:
                if (atGroupStart)
                {
                    state.Destination = RtfDestination.ColorTable;
                    _colorRed = null;
                    _colorGreen = null;
                    _colorBlue = null;
                }
                break;
            case RtfKeyword.AnnotationRangeStart:
                if (atGroupStart)
                    BeginDestination(RtfDestination.AnnotationRangeStart, token, ref state, frames, depth);
                break;
            case RtfKeyword.AnnotationRangeEnd:
                if (atGroupStart)
                    BeginDestination(RtfDestination.AnnotationRangeEnd, token, ref state, frames, depth);
                break;
            case RtfKeyword.AnnotationId:
                if (atGroupStart)
                    BeginDestination(RtfDestination.AnnotationId, token, ref state, frames, depth);
                break;
            case RtfKeyword.AnnotationAuthor:
                if (atGroupStart)
                    BeginDestination(RtfDestination.AnnotationAuthor, token, ref state, frames, depth);
                break;
            case RtfKeyword.AnnotationTime:
                if (atGroupStart)
                {
                    BeginDestination(RtfDestination.AnnotationTime, token, ref state, frames, depth);
                    _activeAnnotationTime = new RtfAnnotationTime(token.Offset);
                }
                break;
            case RtfKeyword.Annotation:
                if (atGroupStart)
                    BeginAnnotation(token, ref state, frames, depth);
                break;
            case RtfKeyword.AnnotationDate:
                if (atGroupStart)
                    BeginDestination(RtfDestination.AnnotationDate, token, ref state, frames, depth);
                break;
            case RtfKeyword.AnnotationReference:
                if (atGroupStart)
                    BeginDestination(RtfDestination.AnnotationReference, token, ref state, frames, depth);
                break;
            case RtfKeyword.AnnotationParent:
                if (atGroupStart)
                    BeginDestination(RtfDestination.AnnotationParent, token, ref state, frames, depth);
                break;
            case RtfKeyword.AnnotationCharacter:
                break;
            case RtfKeyword.Year:
            case RtfKeyword.Month:
            case RtfKeyword.Day:
            case RtfKeyword.Hour:
            case RtfKeyword.Minute:
            case RtfKeyword.Second:
                CaptureAnnotationTime(token, state);
                break;
            case RtfKeyword.StyleSheet:
            case RtfKeyword.Info:
            case RtfKeyword.Generator:
            case RtfKeyword.ThemeData:
            case RtfKeyword.ListTable:
            case RtfKeyword.ListOverrideTable:
                if (atGroupStart)
                    state.Skip = true;
                break;
            case RtfKeyword.Picture:
            case RtfKeyword.AnnotationIcon:
            case RtfKeyword.Object:
            case RtfKeyword.Footnote:
            case RtfKeyword.Header:
            case RtfKeyword.HeaderLeft:
            case RtfKeyword.HeaderRight:
            case RtfKeyword.HeaderFirst:
            case RtfKeyword.Footer:
            case RtfKeyword.FooterLeft:
            case RtfKeyword.FooterRight:
            case RtfKeyword.FooterFirst:
                if (atGroupStart)
                {
                    state.Skip = true;
                    WarnUnsupported(token.Keyword, token.Offset);
                }
                break;
            case RtfKeyword.FieldInstruction:
                if (atGroupStart)
                    state.Skip = true;
                break;
            case RtfKeyword.FieldResult:
            case RtfKeyword.Field:
            case RtfKeyword.SectionDefaults:
            case RtfKeyword.Binary:
                break;
            case RtfKeyword.DefaultFont when token.HasParameter:
                _defaultFontIndex = token.Parameter;
                ApplyDefaultFont();
                break;
            case RtfKeyword.Font when token.HasParameter:
                ApplyFont(token.Parameter, ref state);
                break;
            case RtfKeyword.FontCharset when token.HasParameter && state.Destination == RtfDestination.FontTable:
                state.FontCharset = token.Parameter;
                break;
            case RtfKeyword.Red when token.HasParameter && state.Destination == RtfDestination.ColorTable:
                _colorRed = Math.Clamp(token.Parameter, 0, 255);
                break;
            case RtfKeyword.Green when token.HasParameter && state.Destination == RtfDestination.ColorTable:
                _colorGreen = Math.Clamp(token.Parameter, 0, 255);
                break;
            case RtfKeyword.Blue when token.HasParameter && state.Destination == RtfDestination.ColorTable:
                _colorBlue = Math.Clamp(token.Parameter, 0, 255);
                break;
            case RtfKeyword.Plain:
                state.CharacterFormat = RunFormat.Default;
                break;
            case RtfKeyword.Bold:
                state.CharacterFormat = state.CharacterFormat with { Bold = ToggleValue(token) };
                break;
            case RtfKeyword.Italic:
                state.CharacterFormat = state.CharacterFormat with { Italic = ToggleValue(token) };
                break;
            case RtfKeyword.Caps:
                state.CharacterFormat = state.CharacterFormat with { Caps = ToggleValue(token) };
                break;
            case RtfKeyword.SmallCaps:
                state.CharacterFormat = state.CharacterFormat with { SmallCaps = ToggleValue(token) };
                break;
            case RtfKeyword.Strike:
                state.CharacterFormat = state.CharacterFormat with { Strike = ToggleValue(token) };
                break;
            case RtfKeyword.DoubleStrike:
                state.CharacterFormat = state.CharacterFormat with { DoubleStrike = ToggleValue(token) };
                break;
            case RtfKeyword.Outline:
                state.CharacterFormat = state.CharacterFormat with { Outline = ToggleValue(token) };
                break;
            case RtfKeyword.Shadow:
                state.CharacterFormat = state.CharacterFormat with { Shadow = ToggleValue(token) };
                break;
            case RtfKeyword.Emboss:
                state.CharacterFormat = state.CharacterFormat with { Emboss = ToggleValue(token) };
                break;
            case RtfKeyword.Imprint:
                state.CharacterFormat = state.CharacterFormat with { Imprint = ToggleValue(token) };
                break;
            case RtfKeyword.Hidden:
                state.CharacterFormat = state.CharacterFormat with { Hidden = ToggleValue(token) };
                break;
            case RtfKeyword.WebHidden:
                state.CharacterFormat = state.CharacterFormat with { WebHidden = ToggleValue(token) };
                break;
            case RtfKeyword.NoProof:
                state.CharacterFormat = state.CharacterFormat with { NoProof = ToggleValue(token) };
                break;
            case RtfKeyword.RightToLeftCharacter:
                state.CharacterFormat = state.CharacterFormat with { RightToLeft = true };
                break;
            case RtfKeyword.LeftToRightCharacter:
                state.CharacterFormat = state.CharacterFormat with { RightToLeft = false };
                break;
            case RtfKeyword.FontSize when token.HasParameter && token.Parameter >= 0:
                state.CharacterFormat = state.CharacterFormat with { Size = Length.FromHalfPoints(token.Parameter) };
                break;
            case RtfKeyword.ForegroundColor when token.HasParameter:
                state.CharacterFormat = state.CharacterFormat with { Color = ResolveColor(token.Parameter, token.Offset) };
                break;
            case RtfKeyword.Highlight when token.HasParameter:
                state.CharacterFormat = state.CharacterFormat with { Highlight = ResolveHighlight(token.Parameter, token.Offset) };
                break;
            case RtfKeyword.Underline:
                state.CharacterFormat = state.CharacterFormat with
                {
                    Underline = ToggleValue(token) ? UnderlineStyle.Single : UnderlineStyle.None,
                };
                break;
            case RtfKeyword.UnderlineNone:
                state.CharacterFormat = state.CharacterFormat with { Underline = UnderlineStyle.None };
                break;
            case RtfKeyword.UnderlineWords:
                state.CharacterFormat = state.CharacterFormat with { Underline = UnderlineStyle.Words };
                break;
            case RtfKeyword.UnderlineDouble:
                state.CharacterFormat = state.CharacterFormat with { Underline = UnderlineStyle.Double };
                break;
            case RtfKeyword.UnderlineThick:
                state.CharacterFormat = state.CharacterFormat with { Underline = UnderlineStyle.Thick };
                break;
            case RtfKeyword.UnderlineDotted:
                state.CharacterFormat = state.CharacterFormat with { Underline = UnderlineStyle.Dotted };
                break;
            case RtfKeyword.UnderlineDash:
                state.CharacterFormat = state.CharacterFormat with { Underline = UnderlineStyle.Dash };
                break;
            case RtfKeyword.UnderlineDotDash:
                state.CharacterFormat = state.CharacterFormat with { Underline = UnderlineStyle.DotDash };
                break;
            case RtfKeyword.UnderlineDotDotDash:
                state.CharacterFormat = state.CharacterFormat with { Underline = UnderlineStyle.DotDotDash };
                break;
            case RtfKeyword.UnderlineWave:
                state.CharacterFormat = state.CharacterFormat with { Underline = UnderlineStyle.Wave };
                break;
            case RtfKeyword.UnderlineColor when token.HasParameter:
                state.CharacterFormat = state.CharacterFormat with { UnderlineColor = ResolveColor(token.Parameter, token.Offset) };
                break;
            case RtfKeyword.Subscript:
                state.CharacterFormat = state.CharacterFormat with { VerticalAlignment = VerticalTextAlignment.Subscript };
                break;
            case RtfKeyword.Superscript:
                state.CharacterFormat = state.CharacterFormat with { VerticalAlignment = VerticalTextAlignment.Superscript };
                break;
            case RtfKeyword.NoSuperSub:
                state.CharacterFormat = state.CharacterFormat with { VerticalAlignment = VerticalTextAlignment.Baseline };
                break;
            case RtfKeyword.CharacterSpacing when token.HasParameter:
                state.CharacterFormat = state.CharacterFormat with { CharacterSpacing = Length.FromTwips(token.Parameter) };
                break;
            case RtfKeyword.CharacterScale when token.HasParameter:
                state.CharacterFormat = state.CharacterFormat with { Scale = token.Parameter };
                break;
            case RtfKeyword.Kerning when token.HasParameter:
                state.CharacterFormat = state.CharacterFormat with { Kerning = Length.FromHalfPoints(token.Parameter) };
                break;
            case RtfKeyword.Raise when token.HasParameter:
                state.CharacterFormat = state.CharacterFormat with { Position = Length.FromHalfPoints(token.Parameter) };
                break;
            case RtfKeyword.Lower when token.HasParameter:
                state.CharacterFormat = state.CharacterFormat with { Position = Length.FromHalfPoints(-token.Parameter) };
                break;
            case RtfKeyword.Language when token.HasParameter:
                state.CharacterFormat = state.CharacterFormat with { Language = ResolveLanguage(token.Parameter) };
                break;
            case RtfKeyword.ParagraphDefaults:
                state.ParagraphFormat = ParagraphFormat.Default;
                state.LineSpacingRaw = 0;
                state.TabAlignment = TabAlignment.Left;
                state.TabLeader = TabLeader.None;
                break;
            case RtfKeyword.AlignLeft:
                state.ParagraphFormat = state.ParagraphFormat with { Alignment = ParagraphAlignment.Left };
                break;
            case RtfKeyword.AlignCenter:
                state.ParagraphFormat = state.ParagraphFormat with { Alignment = ParagraphAlignment.Center };
                break;
            case RtfKeyword.AlignRight:
                state.ParagraphFormat = state.ParagraphFormat with { Alignment = ParagraphAlignment.Right };
                break;
            case RtfKeyword.AlignJustify:
                state.ParagraphFormat = state.ParagraphFormat with { Alignment = ParagraphAlignment.Justify };
                break;
            case RtfKeyword.AlignDistribute:
                state.ParagraphFormat = state.ParagraphFormat with { Alignment = ParagraphAlignment.Distribute };
                break;
            case RtfKeyword.LeftIndent when token.HasParameter:
                state.ParagraphFormat = state.ParagraphFormat with { IndentLeft = Length.FromTwips(token.Parameter) };
                break;
            case RtfKeyword.RightIndent when token.HasParameter:
                state.ParagraphFormat = state.ParagraphFormat with { IndentRight = Length.FromTwips(token.Parameter) };
                break;
            case RtfKeyword.FirstLineIndent when token.HasParameter:
                state.ParagraphFormat = token.Parameter < 0
                    ? state.ParagraphFormat with
                    {
                        IndentFirstLine = null,
                        IndentHanging = Length.FromTwips(-token.Parameter),
                    }
                    : state.ParagraphFormat with
                    {
                        IndentFirstLine = Length.FromTwips(token.Parameter),
                        IndentHanging = null,
                    };
                break;
            case RtfKeyword.SpaceBefore when token.HasParameter:
                state.ParagraphFormat = state.ParagraphFormat with { SpacingBefore = Length.FromTwips(token.Parameter) };
                break;
            case RtfKeyword.SpaceAfter when token.HasParameter:
                state.ParagraphFormat = state.ParagraphFormat with { SpacingAfter = Length.FromTwips(token.Parameter) };
                break;
            case RtfKeyword.LineSpacing when token.HasParameter:
                state.LineSpacingRaw = token.Parameter;
                state.ParagraphFormat = token.Parameter == 0
                    ? state.ParagraphFormat with { LineSpacing = null, LineSpacingRule = null }
                    : state.ParagraphFormat with
                    {
                        LineSpacing = Length.FromTwips(Math.Abs(token.Parameter)),
                        LineSpacingRule = token.Parameter < 0 ? LineSpacingRule.Exact : LineSpacingRule.AtLeast,
                    };
                break;
            case RtfKeyword.LineSpacingMultiple when token.HasParameter:
                state.ParagraphFormat = state.ParagraphFormat with
                {
                    LineSpacingRule = token.Parameter != 0
                        ? LineSpacingRule.Auto
                        : state.LineSpacingRaw < 0 ? LineSpacingRule.Exact : LineSpacingRule.AtLeast,
                };
                break;
            case RtfKeyword.KeepTogether:
                state.ParagraphFormat = state.ParagraphFormat with { KeepLinesTogether = ToggleValue(token) };
                break;
            case RtfKeyword.KeepWithNext:
                state.ParagraphFormat = state.ParagraphFormat with { KeepWithNext = ToggleValue(token) };
                break;
            case RtfKeyword.PageBreakBefore:
                state.ParagraphFormat = state.ParagraphFormat with { PageBreakBefore = ToggleValue(token) };
                break;
            case RtfKeyword.WidowControl:
                state.ParagraphFormat = state.ParagraphFormat with { WidowControl = ToggleValue(token) };
                break;
            case RtfKeyword.NoWidowControl:
                state.ParagraphFormat = state.ParagraphFormat with { WidowControl = false };
                break;
            case RtfKeyword.SuppressLineNumbers:
                state.ParagraphFormat = state.ParagraphFormat with { SuppressLineNumbers = ToggleValue(token) };
                break;
            case RtfKeyword.HyphenateParagraph:
                state.ParagraphFormat = state.ParagraphFormat with { SuppressAutoHyphens = !ToggleValue(token) };
                break;
            case RtfKeyword.ContextualSpacing:
                state.ParagraphFormat = state.ParagraphFormat with { ContextualSpacing = ToggleValue(token) };
                break;
            case RtfKeyword.RightToLeftParagraph:
                state.ParagraphFormat = state.ParagraphFormat with { RightToLeft = true };
                break;
            case RtfKeyword.LeftToRightParagraph:
                state.ParagraphFormat = state.ParagraphFormat with { RightToLeft = false };
                break;
            case RtfKeyword.OutlineLevel when token.HasParameter:
                state.ParagraphFormat = state.ParagraphFormat with { OutlineLevel = Math.Clamp(token.Parameter, 0, 8) };
                break;
            case RtfKeyword.TabRight:
                state.TabAlignment = TabAlignment.Right;
                break;
            case RtfKeyword.TabCenter:
                state.TabAlignment = TabAlignment.Center;
                break;
            case RtfKeyword.TabDecimal:
                state.TabAlignment = TabAlignment.Decimal;
                break;
            case RtfKeyword.TabLeaderDot:
                state.TabLeader = TabLeader.Dot;
                break;
            case RtfKeyword.TabLeaderMiddleDot:
                state.TabLeader = TabLeader.MiddleDot;
                break;
            case RtfKeyword.TabLeaderHyphen:
                state.TabLeader = TabLeader.Hyphen;
                break;
            case RtfKeyword.TabLeaderUnderline:
                state.TabLeader = TabLeader.Underscore;
                break;
            case RtfKeyword.TabLeaderHeavy:
                state.TabLeader = TabLeader.Heavy;
                break;
            case RtfKeyword.TabPosition when token.HasParameter:
                AddTabStop(token.Parameter, state.TabAlignment, ref state);
                break;
            case RtfKeyword.BarTabPosition when token.HasParameter:
                AddTabStop(token.Parameter, TabAlignment.Bar, ref state);
                break;
            case RtfKeyword.Paragraph:
                if (!ConsumeFallback(ref state))
                    CommitParagraph(force: true, state);
                break;
            case RtfKeyword.Section:
                if (!ConsumeFallback(ref state))
                {
                    CommitParagraph(force: false, state);
                    AddMarkupNode(token.Offset, "section");
                    _section = _document.Sections.Add();
                }
                break;
            case RtfKeyword.Line:
                if (!ConsumeFallback(ref state))
                    AppendObject(new Break { Kind = BreakKind.Line }, state);
                break;
            case RtfKeyword.Page:
                if (!ConsumeFallback(ref state))
                    AppendObject(new Break { Kind = BreakKind.Page }, state);
                break;
            case RtfKeyword.Column:
                if (!ConsumeFallback(ref state))
                    AppendObject(new Break { Kind = BreakKind.Column }, state);
                break;
            case RtfKeyword.Tab:
                if (!ConsumeFallback(ref state))
                    AppendCharacter('\t', state);
                break;
            case RtfKeyword.EmDash:
                AppendSpecial('\u2014', ref state);
                break;
            case RtfKeyword.EnDash:
                AppendSpecial('\u2013', ref state);
                break;
            case RtfKeyword.EmSpace:
                AppendSpecial('\u2003', ref state);
                break;
            case RtfKeyword.EnSpace:
                AppendSpecial('\u2002', ref state);
                break;
            case RtfKeyword.QuarterEmSpace:
                AppendSpecial('\u2005', ref state);
                break;
            case RtfKeyword.Bullet:
                AppendSpecial('\u2022', ref state);
                break;
            case RtfKeyword.LeftQuote:
                AppendSpecial('\u2018', ref state);
                break;
            case RtfKeyword.RightQuote:
                AppendSpecial('\u2019', ref state);
                break;
            case RtfKeyword.LeftDoubleQuote:
                AppendSpecial('\u201C', ref state);
                break;
            case RtfKeyword.RightDoubleQuote:
                AppendSpecial('\u201D', ref state);
                break;
            case RtfKeyword.Unknown when atGroupStart && state.StarDestination:
                state.Skip = true;
                break;
        }

        state.AtGroupStart = false;
    }

    private void ApplySymbol(RtfToken token, ref RtfState state)
    {
        if (state.Skip)
            return;

        switch (token.Symbol)
        {
            case (byte)'*' when state.AtGroupStart:
                state.StarDestination = true;
                return;
            case (byte)'\\':
            case (byte)'{':
            case (byte)'}':
                AppendSpecial((char)token.Symbol, ref state);
                break;
            case (byte)'~':
                AppendSpecial('\u00A0', ref state);
                break;
            case (byte)'-':
                AppendSpecial('\u00AD', ref state);
                break;
            case (byte)'_':
                AppendSpecial('\u2011', ref state);
                break;
        }

        state.AtGroupStart = false;
    }

    private void AppendEncoded(ReadOnlySpan<byte> bytes, ref RtfState state)
    {
        if (state.Skip)
            return;

        if (state.FallbackCharactersToSkip > 0)
        {
            int skipped = Math.Min(state.FallbackCharactersToSkip, bytes.Length);
            bytes = bytes[skipped..];
            state.FallbackCharactersToSkip -= skipped;
        }

        if (bytes.IsEmpty)
            return;

        _pendingCodePage = state.CodePage;
        _cancellationToken.ThrowIfCancellationRequested();
        _encodedText.Write(bytes);
        _cancellationToken.ThrowIfCancellationRequested();
    }

    private void AppendHexByte(byte value, ref RtfState state)
    {
        if (state.Skip)
            return;
        if (ConsumeFallback(ref state))
            return;

        _pendingCodePage = state.CodePage;
        Span<byte> destination = _encodedText.GetSpan(1);
        destination[0] = value;
        _encodedText.Advance(1);
    }

    private void FlushEncodedText(RtfState state)
    {
        if (_encodedText.WrittenCount == 0)
            return;

        _cancellationToken.ThrowIfCancellationRequested();
        Encoding encoding;
        try
        {
            encoding = Encoding.GetEncoding(
                _pendingCodePage,
                EncoderFallback.ReplacementFallback,
                DecoderFallback.ReplacementFallback);
        }
        catch (ArgumentException)
        {
            encoding = Encoding.GetEncoding(1252);
            _diagnostics.Add(
                RtfImportWarningKind.InvalidEncoding,
                $"Code page {_pendingCodePage} is unavailable; Windows-1252 was used instead.",
                _pendingCodePage.ToString(System.Globalization.CultureInfo.InvariantCulture),
                0);
        }

        string text = encoding.GetString(_encodedText.WrittenSpan);
        _cancellationToken.ThrowIfCancellationRequested();
        _encodedText.Clear();
        if (!state.Skip)
            AppendText(text, state);
    }

    private void AppendText(string text, RtfState state)
    {
        if (state.Destination == RtfDestination.FontTable)
        {
            AppendFontTableText(text, state);
            return;
        }

        if (state.Destination == RtfDestination.ColorTable)
        {
            AppendColorTableText(text);
            return;
        }

        if (state.Destination == RtfDestination.Annotation)
        {
            if (_activeAnnotation is not null)
            {
                EnsureTextLimit(text.Length);
                _activeAnnotation.Paragraph.AppendText(text, state.CharacterFormat);
                _textCharacters += text.Length;
            }
            return;
        }

        if (IsCapturedDestination(state.Destination))
        {
            EnsureTextLimit(text.Length);
            _destinationText.Append(text);
            _textCharacters += text.Length;
            return;
        }

        EnsureTextLimit(text.Length);
        _paragraph.AppendText(text, state.CharacterFormat);
        _textCharacters += text.Length;
    }

    private void AppendCharacter(char value, RtfState state)
    {
        if (state.Skip)
            return;
        AppendText(value.ToString(), state);
    }

    private void AppendObject(InlineObject value, RtfState state)
    {
        if (state.Skip)
            return;
        EnsureTextLimit(1);
        if (state.Destination == RtfDestination.Annotation && _activeAnnotation is not null)
            _activeAnnotation.Paragraph.AppendObject(value, state.CharacterFormat);
        else
            _paragraph.AppendObject(value, state.CharacterFormat);
        _textCharacters++;
    }

    private void AppendSpecial(char value, ref RtfState state)
    {
        if (!ConsumeFallback(ref state))
            AppendCharacter(value, state);
    }

    private void EnsureTextLimit(int additional)
    {
        if (additional > _options.MaxTextCharacters - _textCharacters)
            throw new RtfFormatException($"Decoded text exceeds the {_options.MaxTextCharacters}-character limit", 0);
    }

    private void AddMarkupNode(int byteOffset, string kind)
    {
        try
        {
            _loadBudget.AddMarkupNode();
        }
        catch (DocumentLoadLimitException exception)
            when (exception.LimitName == nameof(DocumentLoadBudget.MaxMarkupNodes))
        {
            throw new RtfFormatException(
                $"RTF {kind} count exceeds the {exception.Limit}-node {exception.LimitName} limit",
                byteOffset);
        }
    }

    private void CommitParagraph(bool force, RtfState state)
    {
        if (state.Destination == RtfDestination.Annotation && _activeAnnotation is not null)
        {
            CommitAnnotationParagraph(force, state);
            return;
        }

        if (!force && _paragraph.TextLength == 0)
            return;

        AddMarkupNode(0, "paragraph");
        _paragraph.Format = state.ParagraphFormat;
        _section.Blocks.Add(_paragraph);
        _paragraph = new Paragraph();
    }

    private void BeginDestination(
        RtfDestination destination,
        RtfToken token,
        ref RtfState state,
        RtfGroupFrame[] frames,
        int depth)
    {
        state.Destination = destination;
        if (depth > 0)
        {
            frames[depth].OpensDestination = true;
            frames[depth].OpenedDestination = destination;
        }

        _destinationText.Clear();
        if (token.HasParameter)
            _destinationText.Append(token.Parameter.ToString(CultureInfo.InvariantCulture));
    }

    private void BeginAnnotation(
        RtfToken token,
        ref RtfState state,
        RtfGroupFrame[] frames,
        int depth)
    {
        if (_activeAnnotation is not null)
        {
            state.Skip = true;
            _diagnostics.Add(
                RtfImportWarningKind.MalformedAnnotation,
                "A nested annotation destination was ignored.",
                "annotation-nested",
                token.Offset);
            return;
        }

        BeginDestination(RtfDestination.Annotation, token, ref state, frames, depth);
        RtfGroupFrame frame = depth > 0 ? frames[depth] : default;
        _activeAnnotation = new RtfAnnotationBuilder(
            _annotations.Count,
            token.Offset,
            _pendingAnnotationId,
            _pendingAnnotationAuthor,
            _pendingAnnotationTime,
            new RtfAnnotationPoint(frame.StartParagraph ?? _paragraph, frame.StartOffset));
        _pendingAnnotationId = null;
        _pendingAnnotationAuthor = null;
        _pendingAnnotationTime = null;
    }

    private void CompleteDestination(RtfGroupFrame frame, RtfState state, int byteOffset)
    {
        if (!frame.OpensDestination)
            return;

        string value = _destinationText.ToString().Trim();
        switch (frame.OpenedDestination)
        {
            case RtfDestination.AnnotationRangeStart:
                CompleteAnchor(_annotationStarts, value, frame, "start", byteOffset);
                break;
            case RtfDestination.AnnotationRangeEnd:
                CompleteAnchor(_annotationEnds, value, frame, "end", byteOffset);
                break;
            case RtfDestination.AnnotationId:
                _pendingAnnotationId = EmptyToNull(value);
                break;
            case RtfDestination.AnnotationAuthor:
                _pendingAnnotationAuthor = EmptyToNull(value);
                break;
            case RtfDestination.AnnotationTime:
                _pendingAnnotationTime = _activeAnnotationTime;
                _activeAnnotationTime = null;
                break;
            case RtfDestination.AnnotationDate:
                if (_activeAnnotation is not null)
                    _activeAnnotation.PackedDate = DecodeDttm(value, byteOffset);
                break;
            case RtfDestination.AnnotationReference:
                if (_activeAnnotation is not null)
                    _activeAnnotation.Reference = EmptyToNull(value);
                break;
            case RtfDestination.AnnotationParent:
                if (_activeAnnotation is not null)
                    _activeAnnotation.ParentReference = EmptyToNull(value);
                break;
            case RtfDestination.Annotation:
                CompleteAnnotation(state);
                break;
        }

        if (frame.OpenedDestination != RtfDestination.Annotation)
            _destinationText.Clear();
    }

    private void CompleteAnchor(
        Dictionary<string, RtfAnnotationPoint> anchors,
        string name,
        RtfGroupFrame frame,
        string kind,
        int byteOffset)
    {
        if (name.Length == 0)
        {
            _diagnostics.Add(
                RtfImportWarningKind.MalformedAnnotation,
                $"An annotation range {kind} has no identifier and was ignored.",
                "annotation-anchor",
                byteOffset);
            return;
        }

        AddMarkupNode(byteOffset, $"annotation range {kind}");
        var point = new RtfAnnotationPoint(frame.StartParagraph ?? _paragraph, frame.StartOffset);
        if (!anchors.TryAdd(name, point))
        {
            _diagnostics.Add(
                RtfImportWarningKind.MalformedAnnotation,
                $"Annotation range identifier '{name}' has more than one {kind}; the first was used.",
                "annotation-anchor",
                byteOffset);
        }
    }

    private void CompleteAnnotation(RtfState state)
    {
        if (_activeAnnotation is null)
            return;

        CommitAnnotationParagraph(force: false, state);
        if (_activeAnnotation.Blocks.Count == 0)
        {
            AddMarkupNode(_activeAnnotation.ByteOffset, "annotation paragraph");
            _activeAnnotation.Blocks.Add(new Paragraph());
        }
        _activeAnnotation.Date = ResolveAnnotationDate(_activeAnnotation);
        AddMarkupNode(_activeAnnotation.ByteOffset, "annotation record");
        _annotations.Add(_activeAnnotation.Build());
        _activeAnnotation = null;
    }

    private void CaptureAnnotationTime(RtfToken token, RtfState state)
    {
        if (state.Destination != RtfDestination.AnnotationTime ||
            _activeAnnotationTime is null ||
            !token.HasParameter)
        {
            return;
        }

        switch (token.Keyword)
        {
            case RtfKeyword.Year:
                _activeAnnotationTime.Year = token.Parameter;
                break;
            case RtfKeyword.Month:
                _activeAnnotationTime.Month = token.Parameter;
                break;
            case RtfKeyword.Day:
                _activeAnnotationTime.Day = token.Parameter;
                break;
            case RtfKeyword.Hour:
                _activeAnnotationTime.Hour = token.Parameter;
                break;
            case RtfKeyword.Minute:
                _activeAnnotationTime.Minute = token.Parameter;
                break;
            case RtfKeyword.Second:
                _activeAnnotationTime.Second = token.Parameter;
                break;
        }
    }

    private DateTimeOffset? ResolveAnnotationDate(RtfAnnotationBuilder annotation)
    {
        DateTimeOffset? packed = annotation.PackedDate;
        RtfAnnotationTime? time = annotation.AnnotationTime;
        if (time is null)
            return packed;

        if (!time.HasAnyValue)
        {
            _diagnostics.Add(
                RtfImportWarningKind.MalformedAnnotation,
                "An annotation atntime destination contains no date or time fields and was ignored.",
                "annotation-time",
                time.ByteOffset);
            return packed;
        }

        int? year = time.Year ?? packed?.Year;
        int? month = time.Month ?? packed?.Month;
        int? day = time.Day ?? packed?.Day;
        if (year is null || month is null || day is null)
        {
            _diagnostics.Add(
                RtfImportWarningKind.MalformedAnnotation,
                "An annotation atntime destination has no complete date and no valid atndate baseline; it was ignored.",
                "annotation-time",
                time.ByteOffset);
            return packed;
        }

        int hour = time.Hour ?? packed?.Hour ?? 0;
        int minute = time.Minute ?? packed?.Minute ?? 0;
        int second = time.Second ?? 0;
        try
        {
            _ = new DateTimeOffset(year.Value, month.Value, day.Value, hour, minute, second, TimeSpan.Zero);
        }
        catch (ArgumentOutOfRangeException)
        {
            _diagnostics.Add(
                RtfImportWarningKind.MalformedAnnotation,
                "An annotation atntime destination contains invalid calendar fields and was ignored.",
                "annotation-time",
                time.ByteOffset);
            return packed;
        }

        if (packed is DateTimeOffset baseline)
        {
            if (time.ConflictsWith(baseline))
            {
                _diagnostics.Add(
                    RtfImportWarningKind.MalformedAnnotation,
                    "Annotation atntime and atndate fields disagree; the packed atndate fields took precedence.",
                    "annotation-time",
                    time.ByteOffset);
            }

            // atndate is the value Word itself exposes and is authoritative for the fields its
            // packed DTTM carries. atntime can still supply seconds, which DTTM has no slot for.
            return new DateTimeOffset(
                baseline.Year,
                baseline.Month,
                baseline.Day,
                baseline.Hour,
                baseline.Minute,
                second,
                TimeSpan.Zero);
        }

        return new DateTimeOffset(year.Value, month.Value, day.Value, hour, minute, second, TimeSpan.Zero);
    }

    private void CommitAnnotationParagraph(bool force, RtfState state)
    {
        if (_activeAnnotation is null || (!force && _activeAnnotation.Paragraph.TextLength == 0))
            return;

        AddMarkupNode(_activeAnnotation.ByteOffset, "annotation paragraph");
        _activeAnnotation.Paragraph.Format = state.ParagraphFormat;
        _activeAnnotation.Blocks.Add(_activeAnnotation.Paragraph);
        _activeAnnotation.Paragraph = new Paragraph();
    }

    private DateTimeOffset? DecodeDttm(string value, int byteOffset)
    {
        uint packed;
        if (!uint.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out packed))
        {
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int signed))
            {
                _diagnostics.Add(
                    RtfImportWarningKind.MalformedAnnotation,
                    $"Annotation date '{value}' is not a 32-bit DTTM value and was ignored.",
                    "annotation-date",
                    byteOffset);
                return null;
            }

            packed = unchecked((uint)signed);
        }

        int minute = (int)(packed & 0x3F);
        int hour = (int)((packed >> 6) & 0x1F);
        int day = (int)((packed >> 11) & 0x1F);
        int month = (int)((packed >> 16) & 0x0F);
        int year = 1900 + (int)((packed >> 20) & 0x1FF);
        if (minute > 59 || hour > 23 || month is < 1 or > 12 ||
            day is < 1 or > 31 || day > DateTime.DaysInMonth(year, month))
        {
            _diagnostics.Add(
                RtfImportWarningKind.MalformedAnnotation,
                $"Annotation date '{value}' contains invalid DTTM fields and was ignored.",
                "annotation-date",
                byteOffset);
            return null;
        }

        return new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero);
    }

    private void ImportAnnotations()
    {
        _cancellationToken.ThrowIfCancellationRequested();
        if (_annotations.Count == 0)
        {
            ReportOrphanAnchors(new HashSet<string>(StringComparer.Ordinal));
            return;
        }

        bool currentParagraphIsReferenced = IsCurrentParagraphReferenced();
        if (currentParagraphIsReferenced && _paragraph.Parent is null)
        {
            AddMarkupNode(0, "paragraph");
            _section.Blocks.Add(_paragraph);
            _paragraph = new Paragraph();
        }

        var paragraphOrder = new Dictionary<Paragraph, int>(ReferenceEqualityComparer.Instance);
        int paragraphIndex = 0;
        foreach (Section section in _document.Sections)
        {
            foreach (Paragraph paragraph in section.Blocks.Paragraphs)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                paragraphOrder.TryAdd(paragraph, paragraphIndex++);
            }
        }

        var usedAnchors = new HashSet<string>(StringComparer.Ordinal);
        var resolved = new List<RtfResolvedAnnotation>(_annotations.Count);
        foreach (RtfImportedAnnotation annotation in _annotations)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            RtfAnnotationPoint start = annotation.Fallback;
            RtfAnnotationPoint end = annotation.Fallback;
            if (annotation.Reference is string reference)
            {
                usedAnchors.Add(reference);
                bool hasStart = _annotationStarts.TryGetValue(reference, out RtfAnnotationPoint rangeStart);
                bool hasEnd = _annotationEnds.TryGetValue(reference, out RtfAnnotationPoint rangeEnd);
                if (hasStart && hasEnd)
                {
                    start = rangeStart;
                    end = rangeEnd;
                }
                else if (hasStart)
                {
                    start = rangeStart;
                    _diagnostics.Add(
                        RtfImportWarningKind.MalformedAnnotation,
                        $"Annotation '{reference}' has no range end; the annotation position was used as its end.",
                        "annotation-anchor",
                        annotation.ByteOffset);
                }
                else if (hasEnd)
                {
                    start = end = rangeEnd;
                    _diagnostics.Add(
                        RtfImportWarningKind.MalformedAnnotation,
                        $"Annotation '{reference}' has no range start; it was imported as a point comment.",
                        "annotation-anchor",
                        annotation.ByteOffset);
                }
                else
                {
                    _diagnostics.Add(
                        RtfImportWarningKind.MalformedAnnotation,
                        $"Annotation '{reference}' has no matching range bookmark; it was attached at the annotation position.",
                        "annotation-anchor",
                        annotation.ByteOffset);
                }
            }

            if (!paragraphOrder.TryGetValue(start.Paragraph, out int startIndex) ||
                !paragraphOrder.TryGetValue(end.Paragraph, out int endIndex) ||
                startIndex > endIndex ||
                startIndex == endIndex && start.Offset > end.Offset)
            {
                start = end = annotation.Fallback;
                _diagnostics.Add(
                    RtfImportWarningKind.MalformedAnnotation,
                    "An annotation range runs backwards or outside the body and was imported as a point comment.",
                    "annotation-anchor",
                    annotation.ByteOffset);
            }

            resolved.Add(new RtfResolvedAnnotation(annotation, start, end));
        }

        ReportOrphanAnchors(usedAnchors);

        RtfResolvedAnnotation[] sequence = [.. resolved];
        Dictionary<Paragraph, AnnotationOffsetIndex> offsetIndices = BuildAnnotationOffsetIndices(sequence);
        var anchorBatches = new Dictionary<Paragraph, ParagraphAnnotationBatch>(ReferenceEqualityComparer.Instance);
        RunFormat referenceFormat = RunFormat.Default with { StyleId = "CommentReference" };
        int nextCommentId = 1;
        foreach (RtfResolvedAnnotation item in sequence)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            int startOffset = MapAnnotationStart(item.Start, offsetIndices);
            int endOffset = offsetIndices[item.End.Paragraph].MapNextReference(item.End.Offset);
            AddMarkupNode(item.Source.ByteOffset, "comment");
            Comment comment = _document.AddImportedComment(
                nextCommentId++,
                item.Source.Author,
                item.Source.AnnotationId);
            comment.Date = item.Source.Date;
            comment.DateUtc = null;
            comment.Blocks.AddRange(item.Source.Blocks);
            item.Comment = comment;

            int markOrder = checked(item.Source.Sequence * 2);
            GetAnnotationBatch(anchorBatches, item.Start.Paragraph).Marks.Add(
                new ImportedMarkPlacement(
                    startOffset,
                    markOrder,
                    new CommentRangeStart { Id = comment.Id }));
            ParagraphAnnotationBatch endBatch = GetAnnotationBatch(anchorBatches, item.End.Paragraph);
            endBatch.Objects.Add(new ImportedObjectInsertion(
                item.End.Offset,
                item.Source.Sequence,
                new CommentReference { Id = comment.Id },
                referenceFormat,
                UseAppendRunSemantics: item.End.Offset == item.End.Paragraph.TextLength));
            endBatch.Marks.Add(new ImportedMarkPlacement(
                endOffset,
                markOrder + 1,
                new CommentRangeEnd { Id = comment.Id }));
        }

        foreach (ParagraphAnnotationBatch batch in anchorBatches.Values)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            batch.Paragraph.InsertImportedAnchors(batch.Objects, batch.Marks, _cancellationToken);
        }

        ResolveAnnotationParents(sequence);
    }

    private void ResolveAnnotationParents(IReadOnlyList<RtfResolvedAnnotation> sequence)
    {
        var annotationIds = new Dictionary<string, PriorParentEntry>(StringComparer.Ordinal);
        var annotationReferences = new Dictionary<string, PriorParentEntry>(StringComparer.Ordinal);
        var rootsByAnchor = new Dictionary<RtfAnnotationAnchorKey, RtfResolvedAnnotation>();

        foreach (RtfResolvedAnnotation item in sequence)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (item.Source.ParentReference is string parentReference)
            {
                bool ambiguous = false;
                RtfResolvedAnnotation? parent = parentReference == "-1"
                    ? rootsByAnchor.GetValueOrDefault(RtfAnnotationAnchorKey.Create(item))
                    : ResolvePriorParent(annotationIds, annotationReferences, parentReference, out ambiguous);

                if (parent?.Comment is Comment parentComment && item.Comment is Comment comment)
                {
                    comment.ParentId = parentComment.Id;
                }
                else
                {
                    _diagnostics.Add(
                        RtfImportWarningKind.MalformedAnnotation,
                        ambiguous
                            ? $"Annotation parent '{parentReference}' is ambiguous among preceding annotations; the comment was kept at top level."
                            : $"Annotation parent '{parentReference}' does not name a preceding annotation; the comment was kept at top level.",
                        "annotation-parent",
                        item.Source.ByteOffset);
                }
            }

            AddPriorParent(annotationIds, item.Source.AnnotationId, item);
            AddPriorParent(annotationReferences, item.Source.Reference, item);
            if (item.Comment?.ParentId is null)
                rootsByAnchor[RtfAnnotationAnchorKey.Create(item)] = item;
        }
    }

    private static RtfResolvedAnnotation? ResolvePriorParent(
        IReadOnlyDictionary<string, PriorParentEntry> annotationIds,
        IReadOnlyDictionary<string, PriorParentEntry> annotationReferences,
        string parentReference,
        out bool ambiguous)
    {
        if (annotationIds.TryGetValue(parentReference, out PriorParentEntry? byAnnotationId))
        {
            ambiguous = byAnnotationId.Ambiguous;
            return byAnnotationId.Candidate;
        }

        // Only non-conforming writers use atnref here. It remains a fail-soft fallback after
        // the grammar's annotid form has been exhausted.
        if (annotationReferences.TryGetValue(parentReference, out PriorParentEntry? byReference))
        {
            ambiguous = byReference.Ambiguous;
            return byReference.Candidate;
        }

        ambiguous = false;
        return null;
    }

    private static void AddPriorParent(
        Dictionary<string, PriorParentEntry> index,
        string? value,
        RtfResolvedAnnotation candidate)
    {
        if (value is null)
            return;

        if (!index.TryGetValue(value, out PriorParentEntry? entry))
            index.Add(value, new PriorParentEntry(candidate));
        else
            entry.AddDuplicate();
    }

    private void ReportOrphanAnchors(IReadOnlySet<string> usedAnchors)
    {
        if (HasOrphanAnchor(_annotationStarts.Keys, usedAnchors) ||
            HasOrphanAnchor(_annotationEnds.Keys, usedAnchors))
        {
            _diagnostics.Add(
                RtfImportWarningKind.MalformedAnnotation,
                "An annotation range bookmark has no annotation body and was ignored.",
                "annotation-orphan-anchor",
                0);
        }
    }

    private bool IsCurrentParagraphReferenced()
    {
        foreach (RtfImportedAnnotation annotation in _annotations)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (ReferenceEquals(annotation.Fallback.Paragraph, _paragraph))
                return true;
        }

        foreach (RtfAnnotationPoint point in _annotationStarts.Values)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (ReferenceEquals(point.Paragraph, _paragraph))
                return true;
        }

        foreach (RtfAnnotationPoint point in _annotationEnds.Values)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (ReferenceEquals(point.Paragraph, _paragraph))
                return true;
        }

        return false;
    }

    private bool HasOrphanAnchor(IEnumerable<string> anchors, IReadOnlySet<string> usedAnchors)
    {
        foreach (string anchor in anchors)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (!usedAnchors.Contains(anchor))
                return true;
        }

        return false;
    }

    private Dictionary<Paragraph, AnnotationOffsetIndex> BuildAnnotationOffsetIndices(
        IReadOnlyList<RtfResolvedAnnotation> annotations)
    {
        var positions = new Dictionary<Paragraph, List<int>>(ReferenceEqualityComparer.Instance);
        foreach (RtfResolvedAnnotation annotation in annotations)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (!positions.TryGetValue(annotation.End.Paragraph, out List<int>? paragraphPositions))
            {
                paragraphPositions = [];
                positions.Add(annotation.End.Paragraph, paragraphPositions);
            }

            paragraphPositions.Add(annotation.End.Offset);
        }

        var result = new Dictionary<Paragraph, AnnotationOffsetIndex>(ReferenceEqualityComparer.Instance);
        foreach ((Paragraph paragraph, List<int> paragraphPositions) in positions)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            result.Add(paragraph, new AnnotationOffsetIndex(paragraphPositions));
        }

        return result;
    }

    private static int MapAnnotationStart(
        RtfAnnotationPoint point,
        IReadOnlyDictionary<Paragraph, AnnotationOffsetIndex> offsetIndices)
    {
        if (!offsetIndices.TryGetValue(point.Paragraph, out AnnotationOffsetIndex? index))
            return point.Offset;

        return index.MapStart(point.Offset);
    }

    private static ParagraphAnnotationBatch GetAnnotationBatch(
        Dictionary<Paragraph, ParagraphAnnotationBatch> batches,
        Paragraph paragraph)
    {
        if (!batches.TryGetValue(paragraph, out ParagraphAnnotationBatch? batch))
        {
            batch = new ParagraphAnnotationBatch(paragraph);
            batches.Add(paragraph, batch);
        }

        return batch;
    }

    private static bool IsCapturedDestination(RtfDestination destination) =>
        destination is RtfDestination.AnnotationRangeStart or
            RtfDestination.AnnotationRangeEnd or
            RtfDestination.AnnotationId or
            RtfDestination.AnnotationAuthor or
            RtfDestination.AnnotationTime or
            RtfDestination.AnnotationDate or
            RtfDestination.AnnotationReference or
            RtfDestination.AnnotationParent;

    private static string? EmptyToNull(string value) => value.Length == 0 ? null : value;

    private void ApplyFont(int index, ref RtfState state)
    {
        if (state.Destination == RtfDestination.FontTable)
        {
            state.FontIndex = index;
            state.FontCharset = 0;
            state.FontCodePage = 0;
            return;
        }

        if (!_fonts.TryGetValue(index, out RtfFont font))
        {
            _diagnostics.Add(
                RtfImportWarningKind.ContentSkipped,
                $"Font index {index} is not present in the font table.",
                $"f{index}",
                0);
            return;
        }

        state.CodePage = font.CodePage;
        state.CharacterFormat = state.CharacterFormat with
        {
            FontAscii = font.Name,
            FontHighAnsi = font.Name,
            FontEastAsia = font.Name,
            FontComplexScript = font.Name,
        };
    }

    private void AppendFontTableText(string text, RtfState state)
    {
        if (state.FontIndex < 0)
            return;

        if (!_fontNames.TryGetValue(state.FontIndex, out StringBuilder? nameBuilder))
        {
            nameBuilder = new StringBuilder();
            _fontNames.Add(state.FontIndex, nameBuilder);
        }

        foreach (char value in text)
        {
            if (value != ';')
            {
                nameBuilder.Append(value);
                continue;
            }

            string name = nameBuilder.ToString().Trim();
            nameBuilder.Clear();
            if (name.Length == 0)
                continue;

            int codePage = state.FontCodePage > 0
                ? state.FontCodePage
                : CodePageFromCharset(state.FontCharset);
            _fonts[state.FontIndex] = new RtfFont(name, codePage);
            ApplyDefaultFont();
        }
    }

    private void ApplyDefaultFont()
    {
        if (!_fonts.TryGetValue(_defaultFontIndex, out RtfFont font))
            return;

        _document.Styles.DefaultRunFormat = _document.Styles.DefaultRunFormat with
        {
            FontAscii = font.Name,
            FontHighAnsi = font.Name,
            FontEastAsia = font.Name,
            FontComplexScript = font.Name,
            FontAsciiTheme = null,
            FontHighAnsiTheme = null,
            FontEastAsiaTheme = null,
            FontComplexScriptTheme = null,
        };
    }

    private void AppendColorTableText(string text)
    {
        foreach (char value in text)
        {
            if (value != ';')
                continue;

            WordColor color = _colorRed is null && _colorGreen is null && _colorBlue is null
                ? WordColor.Auto
                : WordColor.FromRgb(
                    (byte)(_colorRed ?? 0),
                    (byte)(_colorGreen ?? 0),
                    (byte)(_colorBlue ?? 0));
            _colors.Add(color);
            _colorRed = null;
            _colorGreen = null;
            _colorBlue = null;
        }
    }

    private WordColor ResolveColor(int index, int offset)
    {
        if (index == 0)
            return WordColor.Auto;
        if ((uint)index < (uint)_colors.Count)
            return _colors[index];

        _diagnostics.Add(
            RtfImportWarningKind.ContentSkipped,
            $"Color index {index} is not present in the color table; automatic color was used.",
            $"color-{index}",
            offset);
        return WordColor.Auto;
    }

    private HighlightColor ResolveHighlight(int index, int offset)
    {
        WordColor color = ResolveColor(index, offset);
        if (color.Kind != ColorKind.Rgb)
            return HighlightColor.None;

        return color.Rgb switch
        {
            0x000000 => HighlightColor.Black,
            0x0000FF => HighlightColor.Blue,
            0x00FFFF => HighlightColor.Cyan,
            0x00FF00 => HighlightColor.Green,
            0xFF00FF => HighlightColor.Magenta,
            0xFF0000 => HighlightColor.Red,
            0xFFFF00 => HighlightColor.Yellow,
            0xFFFFFF => HighlightColor.White,
            0x000080 => HighlightColor.DarkBlue,
            0x008080 => HighlightColor.DarkCyan,
            0x008000 => HighlightColor.DarkGreen,
            0x800080 => HighlightColor.DarkMagenta,
            0x800000 => HighlightColor.DarkRed,
            0x808000 => HighlightColor.DarkYellow,
            0x808080 => HighlightColor.DarkGray,
            0xC0C0C0 => HighlightColor.LightGray,
            _ => HighlightColor.None,
        };
    }

    private static string? ResolveLanguage(int languageId)
    {
        try
        {
            return CultureInfo.GetCultureInfo(languageId).Name;
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    private static bool ToggleValue(RtfToken token) => !token.HasParameter || token.Parameter != 0;

    private static void AddTabStop(int position, TabAlignment alignment, ref RtfState state)
    {
        var stops = new List<TabStop>(state.ParagraphFormat.Tabs.Count + 1);
        stops.AddRange(state.ParagraphFormat.Tabs);
        stops.Add(new TabStop(Length.FromTwips(position), alignment, state.TabLeader));
        state.ParagraphFormat = state.ParagraphFormat with { Tabs = new EquatableArray<TabStop>(stops) };
        state.TabAlignment = TabAlignment.Left;
        state.TabLeader = TabLeader.None;
    }

    private static int CodePageFromCharset(int charset) => charset switch
    {
        2 => 42,
        77 => 10000,
        78 => 10001,
        79 => 10003,
        80 => 10008,
        81 => 10002,
        83 => 10005,
        84 => 10004,
        85 => 10006,
        86 => 10081,
        87 => 10021,
        88 => 10029,
        89 => 10007,
        128 => 932,
        129 => 949,
        130 => 1361,
        134 => 936,
        136 => 950,
        161 => 1253,
        162 => 1254,
        163 => 1258,
        177 => 1255,
        178 => 1256,
        186 => 1257,
        204 => 1251,
        222 => 874,
        238 => 1250,
        254 => 437,
        255 => 850,
        _ => 1252,
    };

    private static bool ConsumeFallback(ref RtfState state)
    {
        if (state.FallbackCharactersToSkip <= 0)
            return false;
        state.FallbackCharactersToSkip--;
        return true;
    }

    private void WarnUnsupported(RtfKeyword keyword, int offset)
    {
        string subject = keyword.ToString();
        _diagnostics.Add(
            RtfImportWarningKind.UnsupportedDestination,
            $"The {subject} destination is not imported yet.",
            subject,
            offset);
    }

    private struct RtfState
    {
        public static RtfState Default => new()
        {
            CodePage = 1252,
            UnicodeSkipCount = 1,
            AtGroupStart = true,
            FontIndex = -1,
            CharacterFormat = RunFormat.Default,
            ParagraphFormat = ParagraphFormat.Default,
            TabAlignment = TabAlignment.Left,
            TabLeader = TabLeader.None,
        };

        public int CodePage;
        public int UnicodeSkipCount;
        public int FallbackCharactersToSkip;
        public int FontIndex;
        public int FontCharset;
        public int FontCodePage;
        public int LineSpacingRaw;
        public bool Skip;
        public bool AtGroupStart;
        public bool StarDestination;
        public RtfDestination Destination;
        public RunFormat CharacterFormat;
        public ParagraphFormat ParagraphFormat;
        public TabAlignment TabAlignment;
        public TabLeader TabLeader;
    }

    private readonly record struct RtfFont(string Name, int CodePage);

    private enum RtfDestination : byte
    {
        Body,
        FontTable,
        ColorTable,
        Annotation,
        AnnotationRangeStart,
        AnnotationRangeEnd,
        AnnotationId,
        AnnotationAuthor,
        AnnotationTime,
        AnnotationDate,
        AnnotationReference,
        AnnotationParent,
    }

    private struct RtfGroupFrame
    {
        public bool IsUnicodePair;
        public bool IsUnicodeAlternative;
        public int ChildGroups;
        public bool OpensDestination;
        public RtfDestination OpenedDestination;
        public Paragraph? StartParagraph;
        public int StartOffset;
    }

    private readonly record struct RtfAnnotationPoint(Paragraph Paragraph, int Offset);

    private readonly struct RtfAnnotationAnchorKey : IEquatable<RtfAnnotationAnchorKey>
    {
        private RtfAnnotationAnchorKey(
            Paragraph startParagraph,
            int startOffset,
            Paragraph endParagraph,
            int endOffset)
        {
            StartParagraph = startParagraph;
            StartOffset = startOffset;
            EndParagraph = endParagraph;
            EndOffset = endOffset;
        }

        private Paragraph StartParagraph { get; }

        private int StartOffset { get; }

        private Paragraph EndParagraph { get; }

        private int EndOffset { get; }

        public static RtfAnnotationAnchorKey Create(RtfResolvedAnnotation annotation) => new(
            annotation.Start.Paragraph,
            annotation.Start.Offset,
            annotation.End.Paragraph,
            annotation.End.Offset);

        public bool Equals(RtfAnnotationAnchorKey other) =>
            ReferenceEquals(StartParagraph, other.StartParagraph) &&
            StartOffset == other.StartOffset &&
            ReferenceEquals(EndParagraph, other.EndParagraph) &&
            EndOffset == other.EndOffset;

        public override bool Equals(object? obj) => obj is RtfAnnotationAnchorKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(
            RuntimeHelpers.GetHashCode(StartParagraph),
            StartOffset,
            RuntimeHelpers.GetHashCode(EndParagraph),
            EndOffset);
    }

    private sealed class PriorParentEntry(RtfResolvedAnnotation candidate)
    {
        public RtfResolvedAnnotation? Candidate { get; private set; } = candidate;

        public bool Ambiguous { get; private set; }

        public void AddDuplicate()
        {
            Candidate = null;
            Ambiguous = true;
        }
    }

    private sealed class AnnotationOffsetIndex
    {
        private readonly int[] _positions;
        private readonly Dictionary<int, int> _nextOrdinals = [];

        public AnnotationOffsetIndex(List<int> positions)
        {
            positions.Sort();
            _positions = [.. positions];
        }

        public int MapStart(int position) => checked(position + LowerBound(position));

        public int MapNextReference(int position)
        {
            int first = LowerBound(position);
            if (first >= _positions.Length || _positions[first] != position)
                throw new InvalidOperationException("An annotation endpoint was not indexed.");

            int ordinal = _nextOrdinals.GetValueOrDefault(position);
            _nextOrdinals[position] = checked(ordinal + 1);
            return checked(position + first + ordinal);
        }

        private int LowerBound(int value)
        {
            int low = 0;
            int high = _positions.Length;
            while (low < high)
            {
                int middle = low + ((high - low) >> 1);
                if (_positions[middle] < value)
                    low = middle + 1;
                else
                    high = middle;
            }

            return low;
        }
    }

    private sealed class ParagraphAnnotationBatch(Paragraph paragraph)
    {
        public Paragraph Paragraph { get; } = paragraph;

        public List<ImportedObjectInsertion> Objects { get; } = [];

        public List<ImportedMarkPlacement> Marks { get; } = [];
    }

    private sealed class RtfAnnotationTime(int byteOffset)
    {
        public int ByteOffset { get; } = byteOffset;
        public int? Year { get; set; }
        public int? Month { get; set; }
        public int? Day { get; set; }
        public int? Hour { get; set; }
        public int? Minute { get; set; }
        public int? Second { get; set; }

        public bool HasAnyValue =>
            Year is not null || Month is not null || Day is not null ||
            Hour is not null || Minute is not null || Second is not null;

        public bool ConflictsWith(DateTimeOffset value) =>
            Year is int year && year != value.Year ||
            Month is int month && month != value.Month ||
            Day is int day && day != value.Day ||
            Hour is int hour && hour != value.Hour ||
            Minute is int minute && minute != value.Minute;
    }

    private sealed class RtfAnnotationBuilder
    {
        public RtfAnnotationBuilder(
            int sequence,
            int byteOffset,
            string? annotationId,
            string? author,
            RtfAnnotationTime? annotationTime,
            RtfAnnotationPoint fallback)
        {
            Sequence = sequence;
            ByteOffset = byteOffset;
            AnnotationId = annotationId;
            Author = author;
            AnnotationTime = annotationTime;
            Fallback = fallback;
        }

        public int Sequence { get; }
        public int ByteOffset { get; }
        public string? AnnotationId { get; }
        public string? Author { get; }
        public RtfAnnotationTime? AnnotationTime { get; }
        public RtfAnnotationPoint Fallback { get; }
        public string? Reference { get; set; }
        public string? ParentReference { get; set; }
        public DateTimeOffset? PackedDate { get; set; }
        public DateTimeOffset? Date { get; set; }
        public List<Block> Blocks { get; } = [];
        public Paragraph Paragraph { get; set; } = new();

        public RtfImportedAnnotation Build() => new(
            Sequence,
            ByteOffset,
            AnnotationId,
            Author,
            Reference,
            ParentReference,
            Date,
            Fallback,
            Blocks);
    }

    private sealed record RtfImportedAnnotation(
        int Sequence,
        int ByteOffset,
        string? AnnotationId,
        string? Author,
        string? Reference,
        string? ParentReference,
        DateTimeOffset? Date,
        RtfAnnotationPoint Fallback,
        List<Block> Blocks);

    private sealed class RtfResolvedAnnotation
    {
        public RtfResolvedAnnotation(
            RtfImportedAnnotation source,
            RtfAnnotationPoint start,
            RtfAnnotationPoint end)
        {
            Source = source;
            Start = start;
            End = end;
        }

        public RtfImportedAnnotation Source { get; }
        public RtfAnnotationPoint Start { get; }
        public RtfAnnotationPoint End { get; }
        public Comment? Comment { get; set; }
    }
}
