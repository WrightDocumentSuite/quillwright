using System.Text;
using Quillwright.Diagnostics;
using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Rtf.Parsing;

internal sealed partial class RtfParser
{
    private readonly Dictionary<string, int> _bookmarkIdsByName = new(StringComparer.Ordinal);
    private readonly Dictionary<int, RtfStyleEntry> _styleEntries = [];
    private RtfStyleEntry? _activeStyleEntry;
    private string? _pendingFieldInstruction;
    private int _nextBookmarkId = 1;

    private void EnsurePictureByteLimit(long totalBytes, int byteOffset)
    {
        try
        {
            _loadBudget.EnsureMedia(totalBytes);
        }
        catch (DocumentLoadLimitException exception)
        {
            throw new RtfFormatException(exception.Message, byteOffset);
        }
    }

    private void AddMediaPart(long byteCount, int byteOffset)
    {
        try
        {
            _loadBudget.AddMedia(byteCount);
        }
        catch (DocumentLoadLimitException exception)
        {
            throw new RtfFormatException(exception.Message, byteOffset);
        }
    }

    private void CompleteBookmarkStart(string rawName, RtfGroupFrame frame)
    {
        string name = rawName.Trim();
        if (name.Length == 0)
            return;

        if (!_bookmarkIdsByName.TryGetValue(name, out int id))
        {
            id = _nextBookmarkId++;
            _bookmarkIdsByName.Add(name, id);
        }

        Paragraph paragraph = frame.StartParagraph ?? _paragraph;
        paragraph.AddMark(new BookmarkStart { Id = id, Name = name }, frame.StartOffset);
    }

    private void CompleteBookmarkEnd(string rawName, RtfGroupFrame frame)
    {
        string name = rawName.Trim();
        if (name.Length == 0 || !_bookmarkIdsByName.TryGetValue(name, out int id))
            return;

        Paragraph paragraph = frame.StartParagraph ?? _paragraph;
        paragraph.AddMark(new BookmarkEnd { Id = id }, frame.StartOffset);
    }

    private void CompleteFieldResult(RtfGroupFrame frame, RtfState state)
    {
        string? instruction = _pendingFieldInstruction;
        _pendingFieldInstruction = null;
        if (string.IsNullOrWhiteSpace(instruction) || !TryParseHyperlinkInstruction(instruction, out Hyperlink? hyperlink))
            return;

        Paragraph paragraph = frame.StartParagraph ?? CurrentParagraph(state);
        int start = frame.StartOffset;
        int length = paragraph.TextLength - start;
        if (length > 0 && hyperlink is not null)
            paragraph.AddRange(hyperlink, start, length);
    }

    internal static bool TryParseHyperlinkInstruction(string instruction, out Hyperlink? hyperlink)
    {
        hyperlink = null;
        ReadOnlySpan<char> span = instruction.AsSpan().Trim();
        if (!span.StartsWith("HYPERLINK", StringComparison.OrdinalIgnoreCase))
            return false;

        span = span["HYPERLINK".Length..];
        if (!span.IsEmpty && !char.IsWhiteSpace(span[0]) && span[0] is not ('"' or '\\'))
            return false;

        string? url = null;
        string? anchor = null;
        string? tooltip = null;
        int index = 0;

        while (index < span.Length)
        {
            while (index < span.Length && char.IsWhiteSpace(span[index]))
                index++;
            if (index >= span.Length)
                break;

            if (span[index] == '\\')
            {
                index++;
                char flag = index < span.Length ? char.ToLowerInvariant(span[index++]) : '\0';
                while (index < span.Length && !char.IsWhiteSpace(span[index]) && span[index] != '"')
                    index++;
                string? arg = ReadFieldToken(span, ref index);
                if (flag == 'l' && !string.IsNullOrEmpty(arg))
                    anchor = arg;
                else if (flag == 'o' && !string.IsNullOrEmpty(arg))
                    tooltip = arg;
                continue;
            }

            string? target = ReadFieldToken(span, ref index);
            if (!string.IsNullOrEmpty(target) && url is null)
                url = target;
        }

        if (string.IsNullOrEmpty(url) && string.IsNullOrEmpty(anchor))
            return false;

        hyperlink = new Hyperlink { Url = url, Anchor = anchor, Tooltip = tooltip };
        return true;
    }

    private static string? ReadFieldToken(ReadOnlySpan<char> span, ref int index)
    {
        while (index < span.Length && char.IsWhiteSpace(span[index]))
            index++;
        if (index >= span.Length)
            return null;

        var builder = new StringBuilder();
        if (span[index] == '"')
        {
            index++;
            while (index < span.Length && span[index] != '"')
            {
                if (span[index] == '\\' && index + 1 < span.Length && span[index + 1] is '"' or '\\')
                    index++;
                builder.Append(span[index++]);
            }

            if (index < span.Length && span[index] == '"')
                index++;
            return builder.ToString();
        }

        while (index < span.Length && !char.IsWhiteSpace(span[index]))
            builder.Append(span[index++]);
        return builder.ToString();
    }

    private void ApplyStyleControl(
        RtfToken token,
        bool atGroupStart,
        ref RtfState state,
        RtfGroupFrame[] frames,
        int depth)
    {
        if (state.Destination == RtfDestination.StyleSheet)
        {
            if (_activeStyleEntry is null && (atGroupStart || token.Keyword is RtfKeyword.ParagraphStyle or RtfKeyword.CharacterStyle or RtfKeyword.TableStyle or RtfKeyword.SectionStyle))
            {
                _activeStyleEntry = new RtfStyleEntry();
                if (depth > 0)
                {
                    frames[depth].OpensDestination = true;
                    frames[depth].OpenedDestination = RtfDestination.StyleEntry;
                }
            }

            if (_activeStyleEntry is { } entry)
            {
                switch (token.Keyword)
                {
                    case RtfKeyword.ParagraphStyle when token.HasParameter:
                        (entry.Index, entry.Kind) = (token.Parameter, StyleKind.Paragraph);
                        break;
                    case RtfKeyword.CharacterStyle when token.HasParameter:
                        (entry.Index, entry.Kind) = (token.Parameter, StyleKind.Character);
                        break;
                    case RtfKeyword.TableStyle when token.HasParameter:
                        (entry.Index, entry.Kind) = (token.Parameter, StyleKind.Table);
                        break;
                    case RtfKeyword.StyleBasedOn when token.HasParameter:
                        entry.BasedOnIndex = token.Parameter;
                        break;
                    case RtfKeyword.StyleNext when token.HasParameter:
                        entry.NextIndex = token.Parameter;
                        break;
                    case RtfKeyword.StyleLink when token.HasParameter:
                        entry.LinkIndex = token.Parameter;
                        break;
                    case RtfKeyword.StylePriority when token.HasParameter:
                        entry.Priority = token.Parameter;
                        break;
                    case RtfKeyword.StyleQuickFormat:
                        entry.QuickFormat = true;
                        break;
                    case RtfKeyword.StyleHidden:
                        entry.Hidden = true;
                        break;
                    case RtfKeyword.StyleSemiHidden:
                        entry.SemiHidden = true;
                        break;
                    case RtfKeyword.StyleUnhideUsed:
                        entry.UnhideWhenUsed = true;
                        break;
                }
            }

            return;
        }

        if (!token.HasParameter)
            return;

        if (token.Keyword == RtfKeyword.ParagraphStyle)
        {
            string? styleId = ResolveStyleId(token.Parameter, StyleKind.Paragraph);
            state.ParagraphFormat = state.ParagraphFormat with { StyleId = styleId == "Normal" ? null : styleId };
        }
        else if (token.Keyword == RtfKeyword.CharacterStyle)
        {
            string? styleId = ResolveStyleId(token.Parameter, StyleKind.Character);
            state.CharacterFormat = state.CharacterFormat with { StyleId = styleId == "DefaultParagraphFont" ? null : styleId };
        }
    }

    private void AppendStyleSheetText(string text)
    {
        if (_activeStyleEntry is null)
            _activeStyleEntry = new RtfStyleEntry { Index = 0, Kind = StyleKind.Paragraph };

        foreach (char ch in text)
        {
            if (ch == ';')
            {
                _activeStyleEntry.Terminated = true;
                continue;
            }

            _activeStyleEntry.NameBuilder.Append(ch);
        }
    }

    private void CompleteStyleEntry(RtfState state)
    {
        if (_activeStyleEntry is null)
            return;

        RtfStyleEntry entry = _activeStyleEntry;
        _activeStyleEntry = null;
        string name = entry.NameBuilder.ToString().Trim().TrimEnd(';').Trim();
        if (name.Length == 0)
            return;

        entry.Name = name;
        entry.Id = CanonicalStyleId(name);
        entry.ParagraphFormat = state.ParagraphFormat with { StyleId = null };
        entry.RunFormat = state.CharacterFormat with { StyleId = null };
        _styleEntries[entry.Index] = entry;
    }

    private void FinalizeStyles()
    {
        foreach (RtfStyleEntry entry in _styleEntries.Values)
        {
            string? basedOn = entry.BasedOnIndex is int b && b != 222 && _styleEntries.TryGetValue(b, out RtfStyleEntry? bEntry)
                ? bEntry.Id
                : null;
            string? nextStyle = entry.NextIndex is int n && _styleEntries.TryGetValue(n, out RtfStyleEntry? nEntry)
                ? nEntry.Id
                : null;
            string? linkedStyle = entry.LinkIndex is int l && _styleEntries.TryGetValue(l, out RtfStyleEntry? lEntry)
                ? lEntry.Id
                : null;

            Style existing = _document.Styles.GetOrAdd(entry.Id, entry.Kind);
            existing.Name = entry.Name;
            existing.BasedOn = basedOn ?? existing.BasedOn;
            existing.NextStyle = nextStyle ?? existing.NextStyle;
            existing.LinkedStyle = linkedStyle ?? existing.LinkedStyle;
            if (entry.Priority is int priority)
                existing.Priority = priority;
            if (entry.QuickFormat)
                existing.QuickFormat = true;
            if (entry.Hidden)
                existing.Hidden = true;
            if (entry.SemiHidden)
                existing.SemiHidden = true;
            if (entry.UnhideWhenUsed)
                existing.UnhideWhenUsed = true;
            if (!entry.ParagraphFormat.IsEmpty)
                existing.ParagraphFormat = entry.ParagraphFormat;
            if (!entry.RunFormat.IsEmpty)
                existing.RunFormat = entry.RunFormat;
        }
    }

    private string? ResolveStyleId(int index, StyleKind expectedKind)
    {
        if (_styleEntries.TryGetValue(index, out RtfStyleEntry? entry))
            return entry.Id;
        if (index == 0 && expectedKind == StyleKind.Paragraph)
            return "Normal";
        return null;
    }

    internal static string CanonicalStyleId(string name)
    {
        string trimmed = name.Trim();
        if (trimmed.StartsWith("heading ", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(trimmed.AsSpan(8).Trim(), out int level) &&
            level is >= 1 and <= 9)
        {
            return $"Heading{level}";
        }

        return trimmed.ToLowerInvariant() switch
        {
            "normal" => "Normal",
            "default paragraph font" => "DefaultParagraphFont",
            "normal table" => "TableNormal",
            "table grid" => "TableGrid",
            "no list" => "NoList",
            "no spacing" => "NoSpacing",
            "list paragraph" => "ListParagraph",
            "intense quote" => "IntenseQuote",
            "toc heading" => "TOCHeading",
            "footnote text" => "FootnoteText",
            "endnote text" => "EndnoteText",
            "annotation text" => "CommentText",
            "footnote reference" => "FootnoteReference",
            "endnote reference" => "EndnoteReference",
            "annotation reference" => "CommentReference",
            "annotation subject" => "CommentSubject",
            _ => trimmed.Replace(" ", string.Empty, StringComparison.Ordinal),
        };
    }

    private sealed class RtfStyleEntry
    {
        public int Index { get; set; }
        public StyleKind Kind { get; set; } = StyleKind.Paragraph;
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public StringBuilder NameBuilder { get; } = new();
        public int? BasedOnIndex { get; set; }
        public int? NextIndex { get; set; }
        public int? LinkIndex { get; set; }
        public int? Priority { get; set; }
        public bool QuickFormat { get; set; }
        public bool Hidden { get; set; }
        public bool SemiHidden { get; set; }
        public bool UnhideWhenUsed { get; set; }
        public bool Terminated { get; set; }
        public ParagraphFormat ParagraphFormat { get; set; } = ParagraphFormat.Default;
        public RunFormat RunFormat { get; set; } = RunFormat.Default;
    }
}
