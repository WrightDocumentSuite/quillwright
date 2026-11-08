using System.Buffers;
using System.Text;
using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Rtf.Parsing;

internal sealed partial class RtfParser
{
    private readonly Dictionary<int, RtfListDefinition> _listsById = [];
    private readonly List<RtfListOverrideDefinition> _listOverrides = [];
    private readonly List<Block> _currentCellBlocks = [];
    private readonly List<List<Block>> _currentRowCells = [];
    private readonly RtfRowDefinition _rowDefinition = new();
    private RtfHeaderFooterBuilder? _activeHeaderFooter;
    private RtfNoteBuilder? _activeNote;
    private RtfPictureBuilder? _activePicture;
    private RtfListDefinition? _activeList;
    private RtfListLevelDefinition? _activeListLevel;
    private RtfListOverrideDefinition? _activeListOverride;
    private Length? _docPaperWidth;
    private Length? _docPaperHeight;
    private Length? _docMarginLeft;
    private Length? _docMarginRight;
    private Length? _docMarginTop;
    private Length? _docMarginBottom;
    private Length? _docGutter;
    private PageOrientation? _docOrientation;
    private int _activeColumnIndex;

    private Paragraph CurrentParagraph(RtfState state) => state.Destination switch
    {
        RtfDestination.Annotation when _activeAnnotation is not null => _activeAnnotation.Paragraph,
        RtfDestination.Footnote when _activeNote is not null => _activeNote.Paragraph,
        RtfDestination.HeaderFooter when _activeHeaderFooter is not null => _activeHeaderFooter.Paragraph,
        _ => _paragraph,
    };

    private void ReplaceCurrentParagraph(RtfState state, Paragraph next)
    {
        switch (state.Destination)
        {
            case RtfDestination.Annotation when _activeAnnotation is not null:
                _activeAnnotation.Paragraph = next;
                break;
            case RtfDestination.Footnote when _activeNote is not null:
                _activeNote.Paragraph = next;
                break;
            case RtfDestination.HeaderFooter when _activeHeaderFooter is not null:
                _activeHeaderFooter.Paragraph = next;
                break;
            default:
                _paragraph = next;
                break;
        }
    }

    private IList<Block> ActiveStoryBlocks(RtfState state) => state.Destination switch
    {
        RtfDestination.Annotation when _activeAnnotation is not null => _activeAnnotation.Blocks,
        RtfDestination.Footnote when _activeNote is not null => _activeNote.Blocks,
        RtfDestination.HeaderFooter when _activeHeaderFooter is not null => _activeHeaderFooter.Target.Blocks,
        _ => _section.Blocks,
    };

    private void BeginHeaderFooter(
        RtfToken token,
        ref RtfState state,
        RtfGroupFrame[] frames,
        int depth)
    {
        if (_activeHeaderFooter is not null)
        {
            state.Skip = true;
            return;
        }

        HeaderFooter target = token.Keyword switch
        {
            RtfKeyword.HeaderLeft => _section.Headers.GetOrCreate(HeaderFooterKind.Even),
            RtfKeyword.HeaderFirst => _section.Headers.GetOrCreate(HeaderFooterKind.First),
            RtfKeyword.Header or RtfKeyword.HeaderRight => _section.Headers.GetOrCreate(HeaderFooterKind.Default),
            RtfKeyword.FooterLeft => _section.Footers.GetOrCreate(HeaderFooterKind.Even),
            RtfKeyword.FooterFirst => _section.Footers.GetOrCreate(HeaderFooterKind.First),
            _ => _section.Footers.GetOrCreate(HeaderFooterKind.Default),
        };

        if (token.Keyword is RtfKeyword.HeaderFirst or RtfKeyword.FooterFirst)
            _section.Properties.DifferentFirstPage = true;

        target.Blocks.Clear();
        BeginDestination(RtfDestination.HeaderFooter, token, ref state, frames, depth);
        state.StarDestination = false;
        _activeHeaderFooter = new RtfHeaderFooterBuilder(target, token.Offset);
    }

    private void CommitHeaderFooterParagraph(bool force, RtfState state)
    {
        if (_activeHeaderFooter is null)
            return;

        if (state.InTable)
        {
            CommitTableParagraph(force, state);
            return;
        }

        if (!force && _activeHeaderFooter.Paragraph.TextLength == 0)
            return;

        AddMarkupNode(_activeHeaderFooter.ByteOffset, "header/footer paragraph");
        _activeHeaderFooter.Paragraph.Format = state.ParagraphFormat;
        _activeHeaderFooter.Target.Blocks.Add(_activeHeaderFooter.Paragraph);
        _activeHeaderFooter.Paragraph = new Paragraph();
    }

    private void CompleteHeaderFooter(RtfState state)
    {
        if (_activeHeaderFooter is null)
            return;

        FlushPendingTableContent(state);
        CommitHeaderFooterParagraph(force: false, state);
        if (_activeHeaderFooter.Target.Blocks.Count == 0)
        {
            AddMarkupNode(_activeHeaderFooter.ByteOffset, "header/footer paragraph");
            _activeHeaderFooter.Target.Blocks.Add(new Paragraph { Format = state.ParagraphFormat });
        }

        _activeHeaderFooter = null;
    }

    private void BeginNote(
        RtfToken token,
        ref RtfState state,
        RtfGroupFrame[] frames,
        int depth)
    {
        if (_activeNote is not null)
        {
            state.Skip = true;
            return;
        }

        RtfGroupFrame frame = depth > 0 ? frames[depth] : default;
        Paragraph hostParagraph = frame.StartParagraph ?? _paragraph;
        BeginDestination(RtfDestination.Footnote, token, ref state, frames, depth);
        state.StarDestination = false;
        _activeNote = new RtfNoteBuilder(token.Offset, hostParagraph);
    }

    private void ApplyNoteControl(RtfToken token, RtfState state)
    {
        switch (token.Keyword)
        {
            case RtfKeyword.Endnote when state.Destination == RtfDestination.Footnote && _activeNote is not null:
                _activeNote.IsEndnote = true;
                break;

            case RtfKeyword.FootnoteCharacter:
                if (state.Destination == RtfDestination.Footnote && _activeNote is not null)
                {
                    AppendObject(new NoteNumberMark { IsEndnote = _activeNote.IsEndnote }, state);
                }
                break;

            case RtfKeyword.FootnoteSeparator when state.Destination == RtfDestination.Footnote && _activeNote is not null:
                _activeNote.Kind = NoteKind.Separator;
                AppendObject(new NoteSeparator(), state);
                break;

            case RtfKeyword.FootnoteContinuationSeparator when state.Destination == RtfDestination.Footnote && _activeNote is not null:
                _activeNote.Kind = NoteKind.ContinuationSeparator;
                AppendObject(new NoteSeparator { IsContinuation = true }, state);
                break;
        }
    }

    private void CommitNoteParagraph(bool force, RtfState state)
    {
        if (_activeNote is null)
            return;

        if (state.InTable)
        {
            CommitTableParagraph(force, state);
            return;
        }

        if (!force && _activeNote.Paragraph.TextLength == 0)
            return;

        AddMarkupNode(_activeNote.ByteOffset, "footnote paragraph");
        _activeNote.Paragraph.Format = state.ParagraphFormat;
        _activeNote.Blocks.Add(_activeNote.Paragraph);
        _activeNote.Paragraph = new Paragraph();
    }

    private void CompleteNote(RtfState state)
    {
        if (_activeNote is null)
            return;

        FlushPendingTableContent(state);
        CommitNoteParagraph(force: false, state);
        if (_activeNote.Blocks.Count == 0)
        {
            AddMarkupNode(_activeNote.ByteOffset, "footnote paragraph");
            _activeNote.Blocks.Add(new Paragraph { Format = state.ParagraphFormat });
        }

        RtfNoteBuilder completed = _activeNote;
        _activeNote = null;

        if (completed.Kind != NoteKind.Normal)
            return;

        bool isEndnote = completed.IsEndnote;
        List<Note> targetList = isEndnote ? _document.EndnoteList : _document.FootnoteList;
        EnsureNoteScaffolding(targetList, isEndnote);

        int id = targetList.Count == 0 ? 1 : Math.Max(1, targetList.Max(static n => n.Id) + 1);
        string textStyle = isEndnote ? "EndnoteText" : "FootnoteText";
        string referenceStyle = isEndnote ? "EndnoteReference" : "FootnoteReference";
        _document.Styles.GetOrAdd(textStyle);
        _document.Styles.GetOrAdd(referenceStyle, StyleKind.Character);

        var note = new Note(_document, isEndnote) { Id = id };
        foreach (Block block in completed.Blocks)
        {
            if (block is Paragraph paragraph)
            {
                if (paragraph.Format.StyleId is null)
                    paragraph.Format = paragraph.Format with { StyleId = textStyle };

                foreach ((_, InlineObject inlineObject) in paragraph.Objects)
                {
                    if (inlineObject is NoteNumberMark mark)
                        mark.IsEndnote = isEndnote;
                }
            }

            note.Blocks.Add(block);
        }

        AddMarkupNode(completed.ByteOffset, isEndnote ? "endnote" : "footnote");
        targetList.Add(note);

        EnsureTextLimit(1);
        completed.HostParagraph.AppendObject(
            new NoteReference { Id = note.Id, IsEndnote = isEndnote },
            RunFormat.Default with { StyleId = referenceStyle });
        _textCharacters++;
    }

    private void EnsureNoteScaffolding(List<Note> notes, bool isEndnote)
    {
        if (notes.Count > 0)
            return;

        var separator = new Note(_document, isEndnote) { Id = -1, Kind = NoteKind.Separator };
        separator.AddParagraph().AppendObject(new NoteSeparator());
        var continuation = new Note(_document, isEndnote) { Id = 0, Kind = NoteKind.ContinuationSeparator };
        continuation.AddParagraph().AppendObject(new NoteSeparator { IsContinuation = true });
        notes.Add(separator);
        notes.Add(continuation);
    }

    private void BeginPicture(
        RtfToken token,
        ref RtfState state,
        RtfGroupFrame[] frames,
        int depth)
    {
        if (_activePicture is not null)
        {
            state.Skip = true;
            return;
        }

        RtfGroupFrame frame = depth > 0 ? frames[depth] : default;
        Paragraph targetParagraph = frame.StartParagraph ?? CurrentParagraph(state);
        BeginDestination(RtfDestination.Picture, token, ref state, frames, depth);
        state.StarDestination = false;
        _activePicture = new RtfPictureBuilder(token.Offset, targetParagraph, state.CharacterFormat);
    }

    private void ApplyPictureControl(RtfToken token, RtfState state)
    {
        if (state.Destination != RtfDestination.Picture || _activePicture is null)
            return;

        switch (token.Keyword)
        {
            case RtfKeyword.PngBlip:
                _activePicture.Format = RtfPictureFormat.Png;
                break;
            case RtfKeyword.JpegBlip:
                _activePicture.Format = RtfPictureFormat.Jpeg;
                break;
            case RtfKeyword.EmfBlip:
                _activePicture.Format = RtfPictureFormat.Emf;
                break;
            case RtfKeyword.WmfBlip:
                _activePicture.Format = RtfPictureFormat.Wmf;
                break;
            case RtfKeyword.PictureWidth when token.HasParameter && token.Parameter > 0:
                _activePicture.Width = token.Parameter;
                break;
            case RtfKeyword.PictureHeight when token.HasParameter && token.Parameter > 0:
                _activePicture.Height = token.Parameter;
                break;
            case RtfKeyword.PictureWidthGoal when token.HasParameter && token.Parameter > 0:
                _activePicture.WidthGoalTwips = token.Parameter;
                break;
            case RtfKeyword.PictureHeightGoal when token.HasParameter && token.Parameter > 0:
                _activePicture.HeightGoalTwips = token.Parameter;
                break;
            case RtfKeyword.PictureScaleX when token.HasParameter && token.Parameter > 0:
                _activePicture.ScaleX = token.Parameter;
                break;
            case RtfKeyword.PictureScaleY when token.HasParameter && token.Parameter > 0:
                _activePicture.ScaleY = token.Parameter;
                break;
        }
    }

    /// <summary>
    /// Word keeps an inline picture's name and alternative text as shape properties in
    /// <c>{\*\picprop{\sp{\sn name}{\sv value}}}</c>; anywhere else the group is ignored.
    /// </summary>
    private void BeginPictureProperty(
        RtfToken token,
        ref RtfState state,
        RtfGroupFrame[] frames,
        int depth)
    {
        RtfDestination expected = token.Keyword == RtfKeyword.PictureProperties
            ? RtfDestination.Picture
            : RtfDestination.PictureProperties;
        if (state.Destination != expected || _activePicture is null)
        {
            if (state.StarDestination)
                state.Skip = true;
            return;
        }

        RtfDestination destination = token.Keyword switch
        {
            RtfKeyword.PictureProperties => RtfDestination.PictureProperties,
            RtfKeyword.ShapePropertyName => RtfDestination.ShapePropertyName,
            _ => RtfDestination.ShapePropertyValue,
        };
        BeginDestination(destination, token, ref state, frames, depth);
        state.StarDestination = false;
    }

    private void CompletePictureProperty(RtfDestination destination, string trimmed, string text)
    {
        if (_activePicture is null)
            return;

        if (destination == RtfDestination.ShapePropertyName)
        {
            _activePicture.PropertyName = trimmed;
            return;
        }

        switch (_activePicture.PropertyName)
        {
            case "wzName":
                _activePicture.Name = EmptyToNull(text);
                break;
            case "wzDescription":
                _activePicture.Description = EmptyToNull(text);
                break;
        }

        _activePicture.PropertyName = null;
    }

    private void AppendPictureHex(ReadOnlySpan<byte> hexSpan, int byteOffset)
    {
        if (_activePicture is null)
            return;

        foreach (byte b in hexSpan)
        {
            int nibble = HexNibble(b);
            if (nibble < 0)
                continue;

            if (_activePicture.PendingNibble < 0)
            {
                _activePicture.PendingNibble = nibble;
            }
            else
            {
                EnsurePictureByteLimit(_activePicture.Bytes.WrittenCount + 1L, byteOffset);
                byte value = (byte)((_activePicture.PendingNibble << 4) | nibble);
                _activePicture.PendingNibble = -1;
                Span<byte> dest = _activePicture.Bytes.GetSpan(1);
                dest[0] = value;
                _activePicture.Bytes.Advance(1);
            }
        }
    }

    private void AppendPictureBinary(ReadOnlySpan<byte> binarySpan, int byteOffset)
    {
        if (_activePicture is null || binarySpan.IsEmpty)
            return;

        EnsurePictureByteLimit(_activePicture.Bytes.WrittenCount + (long)binarySpan.Length, byteOffset);
        _activePicture.Bytes.Write(binarySpan);
    }

    private void CompletePicture()
    {
        if (_activePicture is null)
            return;

        RtfPictureBuilder completed = _activePicture;
        _activePicture = null;

        if (completed.Bytes.WrittenCount == 0 ||
            completed.Format is RtfPictureFormat.Emf or RtfPictureFormat.Wmf)
        {
            WarnUnsupported(RtfKeyword.Picture, completed.ByteOffset);
            return;
        }

        byte[] rawBytes = completed.Bytes.WrittenSpan.ToArray();
        ImageData image = ImageData.FromBytes(rawBytes);
        if (image.ContentType == "application/octet-stream")
        {
            WarnUnsupported(RtfKeyword.Picture, completed.ByteOffset);
            return;
        }

        Length? width = completed.WidthGoalTwips > 0
            ? Length.FromTwips((int)Math.Round(completed.WidthGoalTwips * (completed.ScaleX / 100.0)))
            : null;
        Length? height = completed.HeightGoalTwips > 0
            ? Length.FromTwips((int)Math.Round(completed.HeightGoalTwips * (completed.ScaleY / 100.0)))
            : null;

        AddMarkupNode(completed.ByteOffset, "picture");
        AddMediaPart(rawBytes.Length, completed.ByteOffset);
        EnsureTextLimit(1);
        Picture picture = completed.TargetParagraph.AppendPicture(image, width, height, completed.CharacterFormat);
        if (completed.Name is not null)
            picture.Name = completed.Name;
        picture.Description = completed.Description;
        _document.Media.Add(image);
        _textCharacters++;
    }

    private static int HexNibble(byte value) => value switch
    {
        >= (byte)'0' and <= (byte)'9' => value - '0',
        >= (byte)'a' and <= (byte)'f' => value - 'a' + 10,
        >= (byte)'A' and <= (byte)'F' => value - 'A' + 10,
        _ => -1,
    };

    private void ApplyListControl(
        RtfToken token,
        bool atGroupStart,
        ref RtfState state,
        RtfGroupFrame[] frames,
        int depth)
    {
        switch (token.Keyword)
        {
            case RtfKeyword.List when atGroupStart && state.Destination == RtfDestination.ListTable:
                BeginDestination(RtfDestination.List, token, ref state, frames, depth);
                state.StarDestination = false;
                _activeList = new RtfListDefinition();
                break;

            case RtfKeyword.ListId when token.HasParameter:
                if (state.Destination == RtfDestination.List && _activeList is not null)
                    _activeList.ListId = token.Parameter;
                else if (state.Destination == RtfDestination.ListOverride && _activeListOverride is not null)
                    _activeListOverride.ListId = token.Parameter;
                break;

            case RtfKeyword.ListLevel when atGroupStart && state.Destination == RtfDestination.List && _activeList is not null:
                BeginDestination(RtfDestination.ListLevel, token, ref state, frames, depth);
                state.StarDestination = false;
                state.ParagraphFormat = ParagraphFormat.Default;
                state.CharacterFormat = RunFormat.Default;
                _activeListLevel = new RtfListLevelDefinition(_activeList.Levels.Count);
                break;

            case RtfKeyword.LevelNumberFormat when token.HasParameter && _activeListLevel is not null:
            case RtfKeyword.LevelNumberFormatN when token.HasParameter && _activeListLevel is not null:
                _activeListLevel.NumberFormat = MapListNumberFormat(token.Parameter);
                break;

            case RtfKeyword.LevelJustification when token.HasParameter && _activeListLevel is not null:
            case RtfKeyword.LevelJustificationN when token.HasParameter && _activeListLevel is not null:
                _activeListLevel.Alignment = token.Parameter switch
                {
                    1 => ParagraphAlignment.Center,
                    2 => ParagraphAlignment.Right,
                    _ => ParagraphAlignment.Left,
                };
                break;

            case RtfKeyword.LevelStartAt when token.HasParameter:
                if (state.Destination == RtfDestination.ListLevel && _activeListLevel is not null)
                    _activeListLevel.Start = token.Parameter;
                else if (state.Destination == RtfDestination.ListOverrideLevel && _activeListOverride is not null)
                    _activeListOverride.PendingStartAt = token.Parameter;
                break;

            case RtfKeyword.LevelText when atGroupStart && state.Destination == RtfDestination.ListLevel && _activeListLevel is not null:
                BeginDestination(RtfDestination.LevelText, token, ref state, frames, depth);
                state.StarDestination = false;
                break;

            case RtfKeyword.ListOverride when atGroupStart && state.Destination == RtfDestination.ListOverrideTable:
                BeginDestination(RtfDestination.ListOverride, token, ref state, frames, depth);
                state.StarDestination = false;
                _activeListOverride = new RtfListOverrideDefinition();
                break;

            case RtfKeyword.ListOverrideLevel when atGroupStart && state.Destination == RtfDestination.ListOverride && _activeListOverride is not null:
                BeginDestination(RtfDestination.ListOverrideLevel, token, ref state, frames, depth);
                state.StarDestination = false;
                _activeListOverride.BeginLevel();
                break;

            case RtfKeyword.ListOverrideStartAt when state.Destination == RtfDestination.ListOverrideLevel && _activeListOverride is not null:
                _activeListOverride.OverrideStartAt = true;
                break;

            case RtfKeyword.ListStyleId when token.HasParameter:
                if (state.Destination == RtfDestination.ListOverride && _activeListOverride is not null)
                {
                    _activeListOverride.NumId = token.Parameter;
                }
                else if (state.Destination is RtfDestination.Body or RtfDestination.HeaderFooter or RtfDestination.Footnote or RtfDestination.Annotation)
                {
                    state.ParagraphFormat = token.Parameter > 0
                        ? state.ParagraphFormat with
                        {
                            NumberingId = token.Parameter,
                            NumberingLevel = state.ParagraphFormat.NumberingLevel ?? 0,
                        }
                        : state.ParagraphFormat with
                        {
                            NumberingId = null,
                            NumberingLevel = null,
                        };
                }
                break;

            case RtfKeyword.ListLevelIndex when token.HasParameter:
                if (state.Destination is RtfDestination.Body or RtfDestination.HeaderFooter or RtfDestination.Footnote or RtfDestination.Annotation)
                {
                    state.ParagraphFormat = state.ParagraphFormat with
                    {
                        NumberingLevel = Math.Clamp(token.Parameter, 0, 8),
                    };
                }
                break;
        }
    }

    private void CompleteLevelText(string rawText)
    {
        if (_activeListLevel is null)
            return;

        _activeListLevel.Text = DecodeLevelText(rawText, _activeListLevel.NumberFormat, _activeListLevel.Level);
    }

    private static string DecodeLevelText(string raw, ListNumberFormat format, int level)
    {
        ReadOnlySpan<char> span = raw.AsSpan();
        if (span.EndsWith(";"))
            span = span[..^1];

        if (span.Length >= 1 && span[0] < 32 && span[0] == span.Length - 1)
            span = span[1..];

        if (span.IsEmpty)
            return format == ListNumberFormat.Bullet ? "\u2022" : $"%{level + 1}.";

        var builder = new StringBuilder(span.Length + 4);
        foreach (char ch in span)
        {
            if (ch <= 8)
            {
                builder.Append('%').Append((char)('1' + ch));
            }
            else if (format == ListNumberFormat.Bullet && ch is '\u00B7' or '\uF0B7')
            {
                builder.Append('\u2022');
            }
            else
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }

    private void CompleteListLevel(RtfState state)
    {
        if (_activeList is null || _activeListLevel is null)
            return;

        RtfListLevelDefinition completed = _activeListLevel;
        _activeListLevel = null;

        string text = completed.Text ?? (completed.NumberFormat == ListNumberFormat.Bullet
            ? "\u2022"
            : $"%{completed.Level + 1}.");

        _activeList.Levels.Add(new NumberingLevel
        {
            Level = completed.Level,
            Start = completed.Start,
            Format = completed.NumberFormat,
            Alignment = completed.Alignment,
            Text = text,
            ParagraphFormat = state.ParagraphFormat,
            RunFormat = state.CharacterFormat,
        });
    }

    private void CompleteList()
    {
        if (_activeList is null)
            return;

        RtfListDefinition completed = _activeList;
        _activeList = null;

        if (completed.ListId != 0)
            _listsById[completed.ListId] = completed;
    }

    private void CompleteListOverride()
    {
        if (_activeListOverride is null)
            return;

        RtfListOverrideDefinition completed = _activeListOverride;
        _activeListOverride = null;

        if (completed.NumId > 0 && completed.ListId != 0)
            _listOverrides.Add(completed);
    }

    private void FinalizeNumbering()
    {
        if (_listOverrides.Count == 0)
            return;

        var abstractIdsByListId = new Dictionary<int, int>();
        int nextAbstractId = _document.Numbering.Definitions.Count == 0
            ? 0
            : _document.Numbering.Definitions.Max(static a => a.Id) + 1;

        foreach (RtfListOverrideDefinition listOverride in _listOverrides)
        {
            if (!abstractIdsByListId.TryGetValue(listOverride.ListId, out int abstractNumId))
            {
                abstractNumId = nextAbstractId++;
                var abstractDef = new AbstractNumbering { Id = abstractNumId };
                if (_listsById.TryGetValue(listOverride.ListId, out RtfListDefinition? listDef) && listDef.Levels.Count > 0)
                {
                    foreach (NumberingLevel level in listDef.Levels)
                        abstractDef.Levels.Add(level);
                }
                else
                {
                    abstractDef.Levels.Add(new NumberingLevel
                    {
                        Level = 0,
                        Start = 1,
                        Format = ListNumberFormat.Decimal,
                        Text = "%1.",
                    });
                }

                _document.Numbering.Definitions.Add(abstractDef);
                abstractIdsByListId.Add(listOverride.ListId, abstractNumId);
            }

            if (_document.Numbering.Instances.All(i => i.Id != listOverride.NumId))
            {
                var instance = new NumberingInstance
                {
                    Id = listOverride.NumId,
                    AbstractId = abstractNumId,
                };
                foreach ((int level, int start) in listOverride.StartOverrides)
                {
                    if (level is >= 0 and <= 8 && instance.Overrides.All(existing => existing.Level != level))
                    {
                        instance.Overrides.Add(new NumberingLevelOverride
                        {
                            Level = level,
                            StartOverride = start,
                        });
                    }
                }

                _document.Numbering.Instances.Add(instance);
            }
        }
    }

    private static ListNumberFormat MapListNumberFormat(int nfc) => nfc switch
    {
        0 => ListNumberFormat.Decimal,
        1 => ListNumberFormat.UpperRoman,
        2 => ListNumberFormat.LowerRoman,
        3 => ListNumberFormat.UpperLetter,
        4 => ListNumberFormat.LowerLetter,
        5 => ListNumberFormat.Ordinal,
        22 => ListNumberFormat.DecimalZero,
        23 => ListNumberFormat.Bullet,
        255 => ListNumberFormat.None,
        _ => ListNumberFormat.Decimal,
    };

    private void ApplyTableControl(RtfToken token, ref RtfState state)
    {
        switch (token.Keyword)
        {
            case RtfKeyword.TableRowDefaults:
                _rowDefinition.Reset();
                break;

            case RtfKeyword.InTable:
                state.InTable = ToggleValue(token);
                break;

            case RtfKeyword.TableNestingLevel when token.HasParameter:
                state.InTable = token.Parameter > 0;
                break;

            case RtfKeyword.TableRowHeight when token.HasParameter:
                _rowDefinition.HeightTwips = token.Parameter;
                break;

            case RtfKeyword.TableRowHeader:
                _rowDefinition.IsHeader = ToggleValue(token);
                break;

            case RtfKeyword.TableRowKeep:
                _rowDefinition.CannotSplit = ToggleValue(token);
                break;

            case RtfKeyword.TableRowAlignLeft:
                _rowDefinition.Alignment = TableAlignment.Left;
                break;

            case RtfKeyword.TableRowAlignCenter:
                _rowDefinition.Alignment = TableAlignment.Center;
                break;

            case RtfKeyword.TableRowAlignRight:
                _rowDefinition.Alignment = TableAlignment.Right;
                break;

            case RtfKeyword.TableRowLeft when token.HasParameter:
                _rowDefinition.LeftTwips = token.Parameter;
                break;

            case RtfKeyword.CellMergeFirst:
                _rowDefinition.PendingCell.HorizontalMerge = RtfHorizontalMerge.Start;
                break;

            case RtfKeyword.CellMerged:
                _rowDefinition.PendingCell.HorizontalMerge = RtfHorizontalMerge.Continue;
                break;

            case RtfKeyword.CellVerticalMergeFirst:
                _rowDefinition.PendingCell.VerticalMerge = VerticalMerge.Restart;
                break;

            case RtfKeyword.CellVerticalMerged:
                _rowDefinition.PendingCell.VerticalMerge = VerticalMerge.Continue;
                break;

            case RtfKeyword.CellBackgroundColor when token.HasParameter:
                WordColor fill = ResolveColor(token.Parameter, token.Offset);
                if (fill.Kind != ColorKind.Auto)
                    _rowDefinition.PendingCell.Background = fill;
                break;

            case RtfKeyword.CellVerticalAlignTop:
                _rowDefinition.PendingCell.VerticalAlignment = VerticalCellAlignment.Top;
                break;

            case RtfKeyword.CellVerticalAlignCenter:
                _rowDefinition.PendingCell.VerticalAlignment = VerticalCellAlignment.Center;
                break;

            case RtfKeyword.CellVerticalAlignBottom:
                _rowDefinition.PendingCell.VerticalAlignment = VerticalCellAlignment.Bottom;
                break;

            case RtfKeyword.RowBorderTop:
                _rowDefinition.ActiveBorderTarget = RtfBorderTarget.RowTop;
                state.ParagraphBorderTarget = RtfParagraphBorderTarget.None;
                break;
            case RtfKeyword.RowBorderLeft:
                _rowDefinition.ActiveBorderTarget = RtfBorderTarget.RowLeft;
                state.ParagraphBorderTarget = RtfParagraphBorderTarget.None;
                break;
            case RtfKeyword.RowBorderBottom:
                _rowDefinition.ActiveBorderTarget = RtfBorderTarget.RowBottom;
                state.ParagraphBorderTarget = RtfParagraphBorderTarget.None;
                break;
            case RtfKeyword.RowBorderRight:
                _rowDefinition.ActiveBorderTarget = RtfBorderTarget.RowRight;
                state.ParagraphBorderTarget = RtfParagraphBorderTarget.None;
                break;
            case RtfKeyword.RowBorderHorizontal:
                _rowDefinition.ActiveBorderTarget = RtfBorderTarget.RowHorizontal;
                state.ParagraphBorderTarget = RtfParagraphBorderTarget.None;
                break;
            case RtfKeyword.RowBorderVertical:
                _rowDefinition.ActiveBorderTarget = RtfBorderTarget.RowVertical;
                state.ParagraphBorderTarget = RtfParagraphBorderTarget.None;
                break;
            case RtfKeyword.CellBorderTop:
                _rowDefinition.ActiveBorderTarget = RtfBorderTarget.CellTop;
                state.ParagraphBorderTarget = RtfParagraphBorderTarget.None;
                break;
            case RtfKeyword.CellBorderLeft:
                _rowDefinition.ActiveBorderTarget = RtfBorderTarget.CellLeft;
                state.ParagraphBorderTarget = RtfParagraphBorderTarget.None;
                break;
            case RtfKeyword.CellBorderBottom:
                _rowDefinition.ActiveBorderTarget = RtfBorderTarget.CellBottom;
                state.ParagraphBorderTarget = RtfParagraphBorderTarget.None;
                break;
            case RtfKeyword.CellBorderRight:
                _rowDefinition.ActiveBorderTarget = RtfBorderTarget.CellRight;
                state.ParagraphBorderTarget = RtfParagraphBorderTarget.None;
                break;
            case RtfKeyword.ParagraphBorderTop:
                state.ParagraphBorderTarget = RtfParagraphBorderTarget.Top;
                _rowDefinition.ActiveBorderTarget = RtfBorderTarget.None;
                break;
            case RtfKeyword.ParagraphBorderBottom:
                state.ParagraphBorderTarget = RtfParagraphBorderTarget.Bottom;
                _rowDefinition.ActiveBorderTarget = RtfBorderTarget.None;
                break;
            case RtfKeyword.ParagraphBorderLeft:
                state.ParagraphBorderTarget = RtfParagraphBorderTarget.Left;
                _rowDefinition.ActiveBorderTarget = RtfBorderTarget.None;
                break;
            case RtfKeyword.ParagraphBorderRight:
                state.ParagraphBorderTarget = RtfParagraphBorderTarget.Right;
                _rowDefinition.ActiveBorderTarget = RtfBorderTarget.None;
                break;
            case RtfKeyword.ParagraphBorderBetween:
                state.ParagraphBorderTarget = RtfParagraphBorderTarget.Between;
                _rowDefinition.ActiveBorderTarget = RtfBorderTarget.None;
                break;
            case RtfKeyword.ParagraphBorderBar:
                state.ParagraphBorderTarget = RtfParagraphBorderTarget.Bar;
                _rowDefinition.ActiveBorderTarget = RtfBorderTarget.None;
                break;
            case RtfKeyword.ParagraphBorderBox:
                state.ParagraphBorderTarget = RtfParagraphBorderTarget.Box;
                _rowDefinition.ActiveBorderTarget = RtfBorderTarget.None;
                break;
            case RtfKeyword.ParagraphBackgroundColor when token.HasParameter:
                WordColor paraFill = ResolveColor(token.Parameter, token.Offset);
                state.ParagraphFormat = state.ParagraphFormat with
                {
                    Shading = paraFill.Kind != ColorKind.Auto ? Shading.Solid(paraFill) : null,
                };
                break;

            case RtfKeyword.BorderSingle:
            case RtfKeyword.BorderHairline:
                ApplyBorderStyle(ref state, BorderStyle.Single);
                break;
            case RtfKeyword.BorderThick:
                ApplyBorderStyle(ref state, BorderStyle.Thick);
                break;
            case RtfKeyword.BorderDouble:
                ApplyBorderStyle(ref state, BorderStyle.Double);
                break;
            case RtfKeyword.BorderDotted:
                ApplyBorderStyle(ref state, BorderStyle.Dotted);
                break;
            case RtfKeyword.BorderDashed:
                ApplyBorderStyle(ref state, BorderStyle.Dashed);
                break;
            case RtfKeyword.BorderDashSmall:
                ApplyBorderStyle(ref state, BorderStyle.DashSmallGap);
                break;
            case RtfKeyword.BorderDotDash:
                ApplyBorderStyle(ref state, BorderStyle.DotDash);
                break;
            case RtfKeyword.BorderDotDotDash:
                ApplyBorderStyle(ref state, BorderStyle.DotDotDash);
                break;
            case RtfKeyword.BorderTriple:
                ApplyBorderStyle(ref state, BorderStyle.Triple);
                break;
            case RtfKeyword.BorderWave:
                ApplyBorderStyle(ref state, BorderStyle.Wave);
                break;
            case RtfKeyword.BorderNone:
            case RtfKeyword.BorderNil:
                ApplyBorderStyle(ref state, BorderStyle.None);
                break;
            case RtfKeyword.BorderWidth when token.HasParameter && token.Parameter >= 0:
                ApplyBorderWidth(ref state, Length.FromTwips(token.Parameter));
                break;
            case RtfKeyword.BorderColor when token.HasParameter:
                ApplyBorderColor(ref state, ResolveColor(token.Parameter, token.Offset));
                break;
            case RtfKeyword.BorderSpace when token.HasParameter && token.Parameter >= 0:
                ApplyBorderSpace(ref state, Length.FromTwips(token.Parameter));
                break;

            case RtfKeyword.CellRightBoundary when token.HasParameter:
                _rowDefinition.CommitCellDefinition(token.Parameter);
                break;

            case RtfKeyword.Cell:
            case RtfKeyword.NestedCell:
                if (!ConsumeFallback(ref state))
                    CommitTableCell(token.Offset, ref state);
                break;

            case RtfKeyword.Row:
            case RtfKeyword.NestedRow:
                if (!ConsumeFallback(ref state))
                    CommitTableRow(token.Offset, ref state);
                break;
        }
    }

    private void ApplyBorderStyle(ref RtfState state, BorderStyle style)
    {
        if (state.ParagraphBorderTarget != RtfParagraphBorderTarget.None)
        {
            UpdateParagraphBorder(ref state, line => (line ?? new BorderLine { Width = Length.FromTwips(15) }) with { Style = style });
            return;
        }

        _rowDefinition.ApplyBorderStyle(style);
    }

    private void ApplyBorderWidth(ref RtfState state, Length width)
    {
        if (state.ParagraphBorderTarget != RtfParagraphBorderTarget.None)
        {
            UpdateParagraphBorder(ref state, line => (line ?? new BorderLine { Style = BorderStyle.Single }) with { Width = width });
            return;
        }

        _rowDefinition.ApplyBorderWidth(width);
    }

    private void ApplyBorderColor(ref RtfState state, WordColor color)
    {
        if (state.ParagraphBorderTarget != RtfParagraphBorderTarget.None)
        {
            UpdateParagraphBorder(ref state, line => (line ?? new BorderLine { Style = BorderStyle.Single, Width = Length.FromTwips(15) }) with { Color = color });
            return;
        }

        _rowDefinition.ApplyBorderColor(color);
    }

    private void ApplyBorderSpace(ref RtfState state, Length space)
    {
        if (state.ParagraphBorderTarget != RtfParagraphBorderTarget.None)
        {
            UpdateParagraphBorder(ref state, line => (line ?? new BorderLine { Style = BorderStyle.Single, Width = Length.FromTwips(15) }) with { Space = space });
            return;
        }

        _rowDefinition.ApplyBorderSpace(space);
    }

    private static void UpdateParagraphBorder(ref RtfState state, Func<BorderLine?, BorderLine> updater)
    {
        BorderSet current = state.ParagraphFormat.Borders ?? BorderSet.Empty;
        BorderSet updated = state.ParagraphBorderTarget switch
        {
            RtfParagraphBorderTarget.Top => current with { Top = updater(current.Top) },
            RtfParagraphBorderTarget.Bottom => current with { Bottom = updater(current.Bottom) },
            RtfParagraphBorderTarget.Left => current with { Left = updater(current.Left) },
            RtfParagraphBorderTarget.Right => current with { Right = updater(current.Right) },
            RtfParagraphBorderTarget.Between => current with { InsideHorizontal = updater(current.InsideHorizontal) },
            RtfParagraphBorderTarget.Bar => current with { Bar = updater(current.Bar) },
            RtfParagraphBorderTarget.Box => current with
            {
                Top = updater(current.Top),
                Bottom = updater(current.Bottom),
                Left = updater(current.Left),
                Right = updater(current.Right),
            },
            _ => current,
        };

        state.ParagraphFormat = state.ParagraphFormat with { Borders = updated };
    }

    private void ResetSectionToDefaults(Section section)
    {
        var props = new SectionProperties();
        if (_docPaperWidth is Length pw)
            props.PageWidth = pw;
        if (_docPaperHeight is Length ph)
            props.PageHeight = ph;
        if (_docMarginLeft is Length ml)
            props.Margins.Left = ml;
        if (_docMarginRight is Length mr)
            props.Margins.Right = mr;
        if (_docMarginTop is Length mt)
            props.Margins.Top = mt;
        if (_docMarginBottom is Length mb)
            props.Margins.Bottom = mb;
        if (_docGutter is Length g)
            props.Margins.Gutter = g;
        if (_docOrientation is PageOrientation orient)
            props.Orientation = orient;
        section.Properties = props;
        _activeColumnIndex = 0;
    }

    private void ApplySectionGeometryControl(RtfToken token)
    {
        SectionProperties props = _section.Properties;
        switch (token.Keyword)
        {
            case RtfKeyword.PaperWidth when token.HasParameter && token.Parameter > 0:
                Length pw = Length.FromTwips(token.Parameter);
                _docPaperWidth = pw;
                props.PageWidth = pw;
                break;
            case RtfKeyword.PaperHeight when token.HasParameter && token.Parameter > 0:
                Length ph = Length.FromTwips(token.Parameter);
                _docPaperHeight = ph;
                props.PageHeight = ph;
                break;
            case RtfKeyword.MarginLeft when token.HasParameter && token.Parameter >= 0:
                Length ml = Length.FromTwips(token.Parameter);
                _docMarginLeft = ml;
                props.Margins.Left = ml;
                break;
            case RtfKeyword.MarginRight when token.HasParameter && token.Parameter >= 0:
                Length mr = Length.FromTwips(token.Parameter);
                _docMarginRight = mr;
                props.Margins.Right = mr;
                break;
            case RtfKeyword.MarginTop when token.HasParameter && token.Parameter >= 0:
                Length mt = Length.FromTwips(token.Parameter);
                _docMarginTop = mt;
                props.Margins.Top = mt;
                break;
            case RtfKeyword.MarginBottom when token.HasParameter && token.Parameter >= 0:
                Length mb = Length.FromTwips(token.Parameter);
                _docMarginBottom = mb;
                props.Margins.Bottom = mb;
                break;
            case RtfKeyword.Gutter when token.HasParameter && token.Parameter >= 0:
                Length g = Length.FromTwips(token.Parameter);
                _docGutter = g;
                props.Margins.Gutter = g;
                break;
            case RtfKeyword.Landscape:
                PageOrientation orient = ToggleValue(token) ? PageOrientation.Landscape : PageOrientation.Portrait;
                _docOrientation = orient;
                props.Orientation = orient;
                break;

            case RtfKeyword.SectionPageWidth when token.HasParameter && token.Parameter > 0:
                props.PageWidth = Length.FromTwips(token.Parameter);
                break;
            case RtfKeyword.SectionPageHeight when token.HasParameter && token.Parameter > 0:
                props.PageHeight = Length.FromTwips(token.Parameter);
                break;
            case RtfKeyword.SectionMarginLeft when token.HasParameter && token.Parameter >= 0:
                props.Margins.Left = Length.FromTwips(token.Parameter);
                break;
            case RtfKeyword.SectionMarginRight when token.HasParameter && token.Parameter >= 0:
                props.Margins.Right = Length.FromTwips(token.Parameter);
                break;
            case RtfKeyword.SectionMarginTop when token.HasParameter && token.Parameter >= 0:
                props.Margins.Top = Length.FromTwips(token.Parameter);
                break;
            case RtfKeyword.SectionMarginBottom when token.HasParameter && token.Parameter >= 0:
                props.Margins.Bottom = Length.FromTwips(token.Parameter);
                break;
            case RtfKeyword.SectionGutter when token.HasParameter && token.Parameter >= 0:
                props.Margins.Gutter = Length.FromTwips(token.Parameter);
                break;
            case RtfKeyword.HeaderDistance when token.HasParameter && token.Parameter >= 0:
                props.Margins.Header = Length.FromTwips(token.Parameter);
                break;
            case RtfKeyword.FooterDistance when token.HasParameter && token.Parameter >= 0:
                props.Margins.Footer = Length.FromTwips(token.Parameter);
                break;
            case RtfKeyword.SectionLandscape:
                props.Orientation = ToggleValue(token) ? PageOrientation.Landscape : PageOrientation.Portrait;
                break;

            case RtfKeyword.SectionBreakNone:
                props.Start = SectionStart.Continuous;
                break;
            case RtfKeyword.SectionBreakColumn:
                props.Start = SectionStart.NextColumn;
                break;
            case RtfKeyword.SectionBreakPage:
                props.Start = SectionStart.NextPage;
                break;
            case RtfKeyword.SectionBreakEven:
                props.Start = SectionStart.EvenPage;
                break;
            case RtfKeyword.SectionBreakOdd:
                props.Start = SectionStart.OddPage;
                break;

            case RtfKeyword.Columns when token.HasParameter && token.Parameter >= 1:
                props.Columns.Count = token.Parameter;
                break;
            case RtfKeyword.ColumnSpace when token.HasParameter && token.Parameter >= 0:
                props.Columns.Space = Length.FromTwips(token.Parameter);
                break;
            case RtfKeyword.LineBetweenColumns:
                props.Columns.Separator = ToggleValue(token);
                break;
            case RtfKeyword.ColumnNumber when token.HasParameter && token.Parameter >= 1:
                _activeColumnIndex = token.Parameter - 1;
                break;
            case RtfKeyword.ColumnWidth when token.HasParameter && token.Parameter > 0:
                UpdateCustomColumn(props, _activeColumnIndex, col => col with { Width = Length.FromTwips(token.Parameter) });
                break;
            case RtfKeyword.ColumnSpaceRight when token.HasParameter && token.Parameter >= 0:
                UpdateCustomColumn(props, _activeColumnIndex, col => col with { Space = Length.FromTwips(token.Parameter) });
                break;
        }
    }

    private static void UpdateCustomColumn(SectionProperties props, int columnIndex, Func<TextColumn, TextColumn> updater)
    {
        if (columnIndex < 0 || columnIndex > 64)
            return;

        ColumnLayout current = props.Columns;
        while (current.Columns.Count <= columnIndex)
            current.Columns.Add(new TextColumn(Length.Zero, Length.Zero));

        current.Columns[columnIndex] = updater(current.Columns[columnIndex]);
        current.EqualWidth = false;
        current.Count = Math.Max(current.Count, current.Columns.Count);
    }

    private void CommitTableParagraph(bool force, RtfState state)
    {
        Paragraph active = CurrentParagraph(state);
        if (!force && active.TextLength == 0)
            return;

        AddMarkupNode(0, "table paragraph");
        active.Format = state.ParagraphFormat;
        _currentCellBlocks.Add(active);
        ReplaceCurrentParagraph(state, new Paragraph());
    }

    private void CommitTableCell(int byteOffset, ref RtfState state)
    {
        state.InTable = true;
        Paragraph active = CurrentParagraph(state);
        if (active.TextLength > 0 || _currentCellBlocks.Count == 0)
        {
            AddMarkupNode(byteOffset, "table cell paragraph");
            active.Format = state.ParagraphFormat;
            _currentCellBlocks.Add(active);
            ReplaceCurrentParagraph(state, new Paragraph());
        }

        var cellBlocks = new List<Block>(_currentCellBlocks);
        _currentCellBlocks.Clear();
        _currentRowCells.Add(cellBlocks);
    }

    private void CommitTableRow(int byteOffset, ref RtfState state)
    {
        Paragraph active = CurrentParagraph(state);
        if (_currentCellBlocks.Count > 0 || active.TextLength > 0)
            CommitTableCell(byteOffset, ref state);

        int cellCount = Math.Max(_currentRowCells.Count, _rowDefinition.Cells.Count);
        if (cellCount == 0)
        {
            state.InTable = false;
            return;
        }

        AddMarkupNode(byteOffset, "table row");
        var rowFormat = TableRowFormat.Default;
        if (_rowDefinition.HeightTwips is int heightTwips && heightTwips != 0)
        {
            rowFormat = rowFormat with
            {
                Height = Length.FromTwips(Math.Abs(heightTwips)),
                HeightRule = heightTwips < 0 ? HeightRule.Exact : HeightRule.AtLeast,
            };
        }

        if (_rowDefinition.IsHeader)
            rowFormat = rowFormat with { IsHeader = true };
        if (_rowDefinition.CannotSplit)
            rowFormat = rowFormat with { CannotSplit = true };
        if (_rowDefinition.Alignment is TableAlignment rowAlignment)
            rowFormat = rowFormat with { Alignment = rowAlignment };

        var row = new TableRow { Format = rowFormat };
        var gridColumnWidths = new List<Length>(cellCount);
        int previousRight = _rowDefinition.LeftTwips;
        TableCell? mergeAnchor = null;
        int mergeAnchorTwips = 0;

        for (int index = 0; index < cellCount; index++)
        {
            RtfCellDefinition cellDef = index < _rowDefinition.Cells.Count
                ? _rowDefinition.Cells[index]
                : default;

            int widthTwips = cellDef.RightBoundaryTwips > previousRight
                ? cellDef.RightBoundaryTwips - previousRight
                : 2160;
            if (cellDef.RightBoundaryTwips > previousRight)
                previousRight = cellDef.RightBoundaryTwips;

            gridColumnWidths.Add(Length.FromTwips(widthTwips));

            if (cellDef.HorizontalMerge == RtfHorizontalMerge.Continue && mergeAnchor is not null)
            {
                int newSpan = (mergeAnchor.Format.GridSpan ?? 1) + 1;
                mergeAnchorTwips += widthTwips;
                mergeAnchor.Format = mergeAnchor.Format with
                {
                    GridSpan = newSpan,
                    Width = TableWidth.FromLength(Length.FromTwips(mergeAnchorTwips)),
                };

                if (index < _currentRowCells.Count)
                {
                    foreach (Block block in _currentRowCells[index])
                    {
                        if (block is Paragraph p && p.TextLength > 0)
                            mergeAnchor.Blocks.Add(p);
                    }
                }

                continue;
            }

            var cellFormat = new TableCellFormat
            {
                Width = TableWidth.FromLength(Length.FromTwips(widthTwips)),
                VerticalMerge = cellDef.VerticalMerge != VerticalMerge.None ? cellDef.VerticalMerge : null,
                VerticalAlignment = cellDef.VerticalAlignment,
                Shading = cellDef.Background is WordColor fill ? Shading.Solid(fill) : null,
                Borders = cellDef.Borders is { IsEmpty: false } cellBorders ? cellBorders : null,
            };

            var cell = new TableCell { Format = cellFormat };
            if (index < _currentRowCells.Count && _currentRowCells[index].Count > 0)
            {
                cell.Blocks.AddRange(_currentRowCells[index]);
            }
            else
            {
                cell.Blocks.Add(new Paragraph());
            }

            row.Cells.Add(cell);
            mergeAnchor = cell;
            mergeAnchorTwips = widthTwips;
        }

        _currentRowCells.Clear();

        IList<Block> storyBlocks = ActiveStoryBlocks(state);
        Table table;
        if (storyBlocks.Count > 0 && storyBlocks[^1] is Table existingTable)
        {
            table = existingTable;
            if (table.Format.Borders is null && _rowDefinition.RowBorders is { IsEmpty: false } rowBorders)
                table.Format = table.Format with { Borders = rowBorders };
        }
        else
        {
            AddMarkupNode(byteOffset, "table");
            var tableFormat = TableFormat.Default;
            if (_rowDefinition.Alignment is TableAlignment tableAlignment)
                tableFormat = tableFormat with { Alignment = tableAlignment };
            if (_rowDefinition.LeftTwips != 0)
                tableFormat = tableFormat with { Indent = TableWidth.FromLength(Length.FromTwips(_rowDefinition.LeftTwips)) };
            if (_rowDefinition.RowBorders is { IsEmpty: false } rowBorders)
                tableFormat = tableFormat with { Borders = rowBorders };

            table = new Table { Format = tableFormat };
            storyBlocks.Add(table);
        }

        if (table.Grid.Count < gridColumnWidths.Count)
        {
            table.Grid.Clear();
            foreach (Length colWidth in gridColumnWidths)
                table.Grid.Add(colWidth);
        }

        table.Rows.Add(row);
        state.InTable = false;
    }

    private void FlushPendingTableContent(RtfState state)
    {
        if (_currentRowCells.Count > 0)
        {
            CommitTableRow(0, ref state);
            return;
        }

        if (_currentCellBlocks.Count > 0)
        {
            IList<Block> storyBlocks = ActiveStoryBlocks(state);
            foreach (Block block in _currentCellBlocks)
                storyBlocks.Add(block);
            _currentCellBlocks.Clear();
        }
    }

    private sealed class RtfHeaderFooterBuilder(HeaderFooter target, int byteOffset)
    {
        public HeaderFooter Target { get; } = target;
        public int ByteOffset { get; } = byteOffset;
        public Paragraph Paragraph { get; set; } = new();
    }

    private sealed class RtfNoteBuilder(int byteOffset, Paragraph hostParagraph)
    {
        public int ByteOffset { get; } = byteOffset;
        public Paragraph HostParagraph { get; } = hostParagraph;
        public bool IsEndnote { get; set; }
        public NoteKind Kind { get; set; } = NoteKind.Normal;
        public List<Block> Blocks { get; } = [];
        public Paragraph Paragraph { get; set; } = new();
    }

    private enum RtfPictureFormat : byte
    {
        Unknown = 0,
        Png,
        Jpeg,
        Emf,
        Wmf,
    }

    private sealed class RtfPictureBuilder(int byteOffset, Paragraph targetParagraph, RunFormat characterFormat)
    {
        public int ByteOffset { get; } = byteOffset;
        public Paragraph TargetParagraph { get; } = targetParagraph;
        public RunFormat CharacterFormat { get; } = characterFormat;
        public RtfPictureFormat Format { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int WidthGoalTwips { get; set; }
        public int HeightGoalTwips { get; set; }
        public int ScaleX { get; set; } = 100;
        public int ScaleY { get; set; } = 100;
        public int PendingNibble { get; set; } = -1;
        public ArrayBufferWriter<byte> Bytes { get; } = new();
        public string? PropertyName { get; set; }
        public string? Name { get; set; }
        public string? Description { get; set; }
    }

    private sealed class RtfListDefinition
    {
        public int ListId { get; set; }
        public List<NumberingLevel> Levels { get; } = [];
    }

    private sealed class RtfListLevelDefinition(int level)
    {
        public int Level { get; } = level;
        public int Start { get; set; } = 1;
        public ListNumberFormat NumberFormat { get; set; } = ListNumberFormat.Decimal;
        public ParagraphAlignment Alignment { get; set; } = ParagraphAlignment.Left;
        public string? Text { get; set; }
    }

    private sealed class RtfListOverrideDefinition
    {
        private int _nextLevel;
        private int _currentLevel = -1;

        public int NumId { get; set; }
        public int ListId { get; set; }
        public bool OverrideStartAt { get; set; }
        public int? PendingStartAt { get; set; }
        public List<(int Level, int Start)> StartOverrides { get; } = [];

        public void BeginLevel()
        {
            _currentLevel = _nextLevel++;
            OverrideStartAt = false;
            PendingStartAt = null;
        }

        public void CompleteLevel()
        {
            if (_currentLevel is >= 0 and <= 8 && (OverrideStartAt || PendingStartAt is not null) && PendingStartAt is int startAt)
                StartOverrides.Add((_currentLevel, startAt));
            _currentLevel = -1;
            OverrideStartAt = false;
            PendingStartAt = null;
        }
    }

    private enum RtfHorizontalMerge : byte
    {
        None = 0,
        Start,
        Continue,
    }

    private enum RtfBorderTarget : byte
    {
        None = 0,
        RowTop,
        RowLeft,
        RowBottom,
        RowRight,
        RowHorizontal,
        RowVertical,
        CellTop,
        CellLeft,
        CellBottom,
        CellRight,
    }

    private record struct RtfCellDefinition(
        int RightBoundaryTwips,
        RtfHorizontalMerge HorizontalMerge,
        VerticalMerge VerticalMerge,
        WordColor? Background,
        VerticalCellAlignment? VerticalAlignment,
        BorderSet? Borders);

    private sealed class RtfRowDefinition
    {
        public int? HeightTwips { get; set; }
        public bool IsHeader { get; set; }
        public bool CannotSplit { get; set; }
        public TableAlignment? Alignment { get; set; }
        public int LeftTwips { get; set; }
        public BorderSet? RowBorders { get; set; }
        public RtfBorderTarget ActiveBorderTarget { get; set; }
        public RtfCellDefinition PendingCell;
        public List<RtfCellDefinition> Cells { get; } = [];

        public void Reset()
        {
            HeightTwips = null;
            IsHeader = false;
            CannotSplit = false;
            Alignment = null;
            LeftTwips = 0;
            RowBorders = null;
            ActiveBorderTarget = RtfBorderTarget.None;
            PendingCell = default;
            Cells.Clear();
        }

        public void CommitCellDefinition(int rightBoundaryTwips)
        {
            PendingCell.RightBoundaryTwips = rightBoundaryTwips;
            Cells.Add(PendingCell);
            PendingCell = default;
            ActiveBorderTarget = RtfBorderTarget.None;
        }

        public void ApplyBorderStyle(BorderStyle style) =>
            UpdateActiveBorder(line => (line ?? new BorderLine { Width = Length.FromTwips(15) }) with { Style = style });

        public void ApplyBorderWidth(Length width) =>
            UpdateActiveBorder(line => (line ?? new BorderLine { Style = BorderStyle.Single }) with { Width = width });

        public void ApplyBorderColor(WordColor color) =>
            UpdateActiveBorder(line => (line ?? new BorderLine { Style = BorderStyle.Single, Width = Length.FromTwips(15) }) with { Color = color });

        public void ApplyBorderSpace(Length space) =>
            UpdateActiveBorder(line => (line ?? new BorderLine { Style = BorderStyle.Single, Width = Length.FromTwips(15) }) with { Space = space });

        private void UpdateActiveBorder(Func<BorderLine?, BorderLine> updater)
        {
            switch (ActiveBorderTarget)
            {
                case RtfBorderTarget.RowTop:
                    RowBorders = (RowBorders ?? BorderSet.Empty) with { Top = updater(RowBorders?.Top) };
                    break;
                case RtfBorderTarget.RowLeft:
                    RowBorders = (RowBorders ?? BorderSet.Empty) with { Left = updater(RowBorders?.Left) };
                    break;
                case RtfBorderTarget.RowBottom:
                    RowBorders = (RowBorders ?? BorderSet.Empty) with { Bottom = updater(RowBorders?.Bottom) };
                    break;
                case RtfBorderTarget.RowRight:
                    RowBorders = (RowBorders ?? BorderSet.Empty) with { Right = updater(RowBorders?.Right) };
                    break;
                case RtfBorderTarget.RowHorizontal:
                    RowBorders = (RowBorders ?? BorderSet.Empty) with { InsideHorizontal = updater(RowBorders?.InsideHorizontal) };
                    break;
                case RtfBorderTarget.RowVertical:
                    RowBorders = (RowBorders ?? BorderSet.Empty) with { InsideVertical = updater(RowBorders?.InsideVertical) };
                    break;
                case RtfBorderTarget.CellTop:
                    PendingCell.Borders = (PendingCell.Borders ?? BorderSet.Empty) with { Top = updater(PendingCell.Borders?.Top) };
                    break;
                case RtfBorderTarget.CellLeft:
                    PendingCell.Borders = (PendingCell.Borders ?? BorderSet.Empty) with { Left = updater(PendingCell.Borders?.Left) };
                    break;
                case RtfBorderTarget.CellBottom:
                    PendingCell.Borders = (PendingCell.Borders ?? BorderSet.Empty) with { Bottom = updater(PendingCell.Borders?.Bottom) };
                    break;
                case RtfBorderTarget.CellRight:
                    PendingCell.Borders = (PendingCell.Borders ?? BorderSet.Empty) with { Right = updater(PendingCell.Borders?.Right) };
                    break;
            }
        }
    }
}
