using System.Runtime.InteropServices;
using Quillwright.Styles;

namespace Quillwright.Model;

public sealed partial class Paragraph
{
    /// <summary>Number of runs in the paragraph.</summary>
    internal int RunCount => _runs.Count;

    /// <summary>Inserts text at an offset.</summary>
    /// <param name="offset">Where to insert.</param>
    /// <param name="text">The text.</param>
    /// <param name="format">Formatting of the new text, or <see langword="null"/> to take it from the surrounding run.</param>
    public Paragraph InsertText(int offset, ReadOnlySpan<char> text, RunFormat? format = null) =>
        ReplaceText(offset, 0, text, format);

    /// <summary>
    /// Inserts an object at an offset, shifting everything after it. The object occupies one
    /// character, like every other anchored object.
    /// </summary>
    /// <param name="offset">Where to insert.</param>
    /// <param name="value">The object to anchor.</param>
    /// <param name="format">Formatting of the run that carries it.</param>
    public Paragraph InsertObject(int offset, InlineObject value, RunFormat? format = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        offset = Math.Clamp(offset, 0, _length);
        ReplaceText(offset, 0, [value.PlaceholderChar], format);

        _objects ??= [];
        int index = _objects.FindIndex(anchored => anchored.Offset > offset);
        _objects.Insert(index < 0 ? _objects.Count : index, new AnchoredObject { Offset = offset, Object = value });
        Anchor(value);
        return this;
    }

    /// <summary>
    /// Materializes format-reader-owned objects and marks in one stable pass. Object offsets
    /// address the paragraph before this batch; mark offsets address the final paragraph.
    /// </summary>
    internal void InsertImportedAnchors(
        IReadOnlyList<ImportedObjectInsertion> objectInsertions,
        IReadOnlyList<ImportedMarkPlacement> markPlacements,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(objectInsertions);
        ArgumentNullException.ThrowIfNull(markPlacements);
        cancellationToken.ThrowIfCancellationRequested();

        int oldLength = _length;
        ImportedObjectInsertion[] insertions = new ImportedObjectInsertion[objectInsertions.Count];
        for (int index = 0; index < insertions.Length; index++)
        {
            CheckImportCancellation(cancellationToken, index);
            ImportedObjectInsertion insertion = objectInsertions[index];
            ArgumentNullException.ThrowIfNull(insertion.Object);
            insertions[index] = insertion with
            {
                SourceOffset = Math.Clamp(insertion.SourceOffset, 0, oldLength),
            };
        }
        Array.Sort(insertions, static (left, right) =>
        {
            int offset = left.SourceOffset.CompareTo(right.SourceOffset);
            return offset != 0 ? offset : left.Order.CompareTo(right.Order);
        });
        cancellationToken.ThrowIfCancellationRequested();

        int newLength = checked(oldLength + insertions.Length);
        int[] finalObjectOffsets = new int[insertions.Length];
        RunKind[] insertedKinds = new RunKind[insertions.Length];
        string?[] insertedAttributes = new string?[insertions.Length];
        char[] rebuiltBuffer = newLength == 0
            ? []
            : new char[Math.Max(newLength, _buffer.Length)];
        int sourceOffset = 0;
        int destinationOffset = 0;
        int surroundingRunIndex = 0;
        for (int index = 0; index < insertions.Length; index++)
        {
            CheckImportCancellation(cancellationToken, index);
            ImportedObjectInsertion insertion = insertions[index];
            int copyLength = insertion.SourceOffset - sourceOffset;
            if (copyLength > 0)
            {
                _buffer.AsSpan(sourceOffset, copyLength)
                    .CopyTo(rebuiltBuffer.AsSpan(destinationOffset));
                sourceOffset += copyLength;
                destinationOffset += copyLength;
            }

            finalObjectOffsets[index] = destinationOffset;
            rebuiltBuffer[destinationOffset++] = insertion.Object.PlaceholderChar;

            if (insertion.UseAppendRunSemantics)
            {
                insertedKinds[index] = RunKind.Text;
                continue;
            }

            while (surroundingRunIndex < _runs.Count - 1 &&
                   _runs[surroundingRunIndex].End <= insertion.SourceOffset)
            {
                surroundingRunIndex++;
            }
            if ((uint)surroundingRunIndex < (uint)_runs.Count)
            {
                insertedKinds[index] = _runs[surroundingRunIndex].Kind;
                insertedAttributes[index] = _runs[surroundingRunIndex].Attributes;
            }
        }

        if (sourceOffset < oldLength)
        {
            _buffer.AsSpan(sourceOffset, oldLength - sourceOffset)
                .CopyTo(rebuiltBuffer.AsSpan(destinationOffset));
        }

        var rebuiltRuns = new List<RunSpan>(_runs.Count + insertions.Length);
        int nextInsertion = 0;
        foreach (RunSpan run in _runs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            while (nextInsertion < insertions.Length &&
                   insertions[nextInsertion].SourceOffset < run.Start)
            {
                AddImportedRun(rebuiltRuns, insertions[nextInsertion], finalObjectOffsets[nextInsertion],
                    insertedKinds[nextInsertion], insertedAttributes[nextInsertion]);
                nextInsertion++;
            }

            int runSourceOffset = run.Start;
            while (nextInsertion < insertions.Length &&
                   insertions[nextInsertion].SourceOffset < run.End)
            {
                int insertionOffset = insertions[nextInsertion].SourceOffset;
                AddImportedRunSegment(rebuiltRuns, run, runSourceOffset, insertionOffset, nextInsertion);
                do
                {
                    CheckImportCancellation(cancellationToken, nextInsertion);
                    AddImportedRun(rebuiltRuns, insertions[nextInsertion], finalObjectOffsets[nextInsertion],
                        insertedKinds[nextInsertion], insertedAttributes[nextInsertion]);
                    nextInsertion++;
                }
                while (nextInsertion < insertions.Length &&
                       insertions[nextInsertion].SourceOffset == insertionOffset);
                runSourceOffset = insertionOffset;
            }

            AddImportedRunSegment(rebuiltRuns, run, runSourceOffset, run.End, nextInsertion);
        }

        while (nextInsertion < insertions.Length)
        {
            CheckImportCancellation(cancellationToken, nextInsertion);
            AddImportedRun(rebuiltRuns, insertions[nextInsertion], finalObjectOffsets[nextInsertion],
                insertedKinds[nextInsertion], insertedAttributes[nextInsertion]);
            nextInsertion++;
        }

        var rebuiltObjects = new List<AnchoredObject>((_objects?.Count ?? 0) + insertions.Length);
        int newObjectIndex = 0;
        if (_objects is not null)
        {
            int existingObjectIndex = 0;
            foreach (AnchoredObject existing in _objects)
            {
                CheckImportCancellation(cancellationToken, existingObjectIndex++);
                while (newObjectIndex < insertions.Length &&
                       insertions[newObjectIndex].SourceOffset <= existing.Offset)
                {
                    AddImportedObject(rebuiltObjects, insertions[newObjectIndex], finalObjectOffsets[newObjectIndex]);
                    newObjectIndex++;
                }

                rebuiltObjects.Add(new AnchoredObject
                {
                    Offset = checked(existing.Offset + newObjectIndex),
                    Object = existing.Object,
                });
            }
        }
        while (newObjectIndex < insertions.Length)
        {
            CheckImportCancellation(cancellationToken, newObjectIndex);
            AddImportedObject(rebuiltObjects, insertions[newObjectIndex], finalObjectOffsets[newObjectIndex]);
            newObjectIndex++;
        }

        ImportedMarkPlacement[] importedMarks = new ImportedMarkPlacement[markPlacements.Count];
        for (int index = 0; index < importedMarks.Length; index++)
        {
            CheckImportCancellation(cancellationToken, index);
            ImportedMarkPlacement placement = markPlacements[index];
            ArgumentNullException.ThrowIfNull(placement.Mark);
            importedMarks[index] = placement with
            {
                FinalOffset = Math.Clamp(placement.FinalOffset, 0, newLength),
            };
        }
        Array.Sort(importedMarks, static (left, right) =>
        {
            int offset = left.FinalOffset.CompareTo(right.FinalOffset);
            return offset != 0 ? offset : left.Order.CompareTo(right.Order);
        });
        cancellationToken.ThrowIfCancellationRequested();

        var rebuiltMarks = new List<AnchoredMark>((_marks?.Count ?? 0) + importedMarks.Length);
        int existingMarkIndex = 0;
        int importedMarkIndex = 0;
        while ((_marks is not null && existingMarkIndex < _marks.Count) ||
               importedMarkIndex < importedMarks.Length)
        {
            CheckImportCancellation(cancellationToken, existingMarkIndex + importedMarkIndex);
            int existingFinalOffset = _marks is not null && existingMarkIndex < _marks.Count
                ? checked(_marks[existingMarkIndex].Offset +
                    LowerBound(insertions, _marks[existingMarkIndex].Offset))
                : int.MaxValue;
            int importedFinalOffset = importedMarkIndex < importedMarks.Length
                ? importedMarks[importedMarkIndex].FinalOffset
                : int.MaxValue;
            if (existingFinalOffset <= importedFinalOffset)
            {
                rebuiltMarks.Add(new AnchoredMark
                {
                    Offset = existingFinalOffset,
                    Mark = _marks![existingMarkIndex++].Mark,
                });
            }
            else
            {
                ImportedMarkPlacement placement = importedMarks[importedMarkIndex++];
                rebuiltMarks.Add(new AnchoredMark { Offset = placement.FinalOffset, Mark = placement.Mark });
            }
        }

        if (_ranges is not null)
        {
            Span<AnchoredRange> ranges = CollectionsMarshal.AsSpan(_ranges);
            for (int index = 0; index < ranges.Length; index++)
            {
                CheckImportCancellation(cancellationToken, index);
                int start = checked(ranges[index].Start + LowerBound(insertions, ranges[index].Start));
                int end = checked(ranges[index].End + LowerBound(insertions, ranges[index].End));
                ranges[index].Start = start;
                ranges[index].Length = end - start;
            }
        }

        _buffer = rebuiltBuffer;
        _length = newLength;
        _cachedText = null;
        _runs.Clear();
        _runs.AddRange(rebuiltRuns);
        _objects = rebuiltObjects.Count == 0 ? null : rebuiltObjects;
        _marks = rebuiltMarks.Count == 0 ? null : rebuiltMarks;
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static void AddImportedRunSegment(
        List<RunSpan> target,
        RunSpan source,
        int sourceStart,
        int sourceEnd,
        int insertedBefore)
    {
        if (sourceEnd <= sourceStart)
            return;
        AddImportedRun(target, new RunSpan
        {
            Start = checked(sourceStart + insertedBefore),
            Length = sourceEnd - sourceStart,
            Format = source.Format,
            Kind = source.Kind,
            Attributes = source.Attributes,
        });
    }

    private static void AddImportedRun(
        List<RunSpan> target,
        ImportedObjectInsertion insertion,
        int finalOffset,
        RunKind kind,
        string? attributes) =>
        AddImportedRun(target, new RunSpan
        {
            Start = finalOffset,
            Length = 1,
            Format = insertion.Format,
            Kind = kind,
            Attributes = attributes,
        });

    private static void AddImportedRun(List<RunSpan> target, RunSpan run)
    {
        if (run.Length <= 0)
            return;

        if (target.Count > 0)
        {
            RunSpan previous = target[^1];
            if (previous.End == run.Start &&
                previous.Kind == run.Kind &&
                ReferenceEquals(previous.Format, run.Format) &&
                previous.Attributes == run.Attributes)
            {
                previous.Length = checked(previous.Length + run.Length);
                target[^1] = previous;
                return;
            }
        }

        target.Add(run);
    }

    private void AddImportedObject(
        List<AnchoredObject> target,
        ImportedObjectInsertion insertion,
        int finalOffset)
    {
        target.Add(new AnchoredObject { Offset = finalOffset, Object = insertion.Object });
        Anchor(insertion.Object);
    }

    private static int LowerBound(ImportedObjectInsertion[] insertions, int value)
    {
        int low = 0;
        int high = insertions.Length;
        while (low < high)
        {
            int middle = low + ((high - low) >> 1);
            if (insertions[middle].SourceOffset < value)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    private static void CheckImportCancellation(CancellationToken cancellationToken, int index)
    {
        if ((index & 0xFFF) == 0)
            cancellationToken.ThrowIfCancellationRequested();
    }

    /// <summary>Character formatting of the run covering an offset.</summary>
    /// <param name="offset">Offset to look at.</param>
    public RunFormat FormatAtOffset(int offset) => FormatAt(Math.Clamp(offset, 0, Math.Max(0, _length - 1)));

    /// <summary>Removes a stretch of text along with the objects anchored inside it.</summary>
    /// <param name="offset">Offset of the first character to remove.</param>
    /// <param name="count">How many characters to remove.</param>
    public Paragraph RemoveText(int offset, int count) => ReplaceText(offset, count, default, null);

    /// <summary>
    /// Replaces anchored instances wholesale, without touching the text they anchor to. This
    /// is what moving content between documents needs: a clone shares its anchors with the
    /// original, so anything that must differ per document — an id, a relationship — has to be
    /// swapped for a fresh instance rather than mutated in place.
    /// </summary>
    /// <param name="replaceObject">
    /// Maps each object to its replacement; <see langword="null"/> removes the object and its
    /// placeholder character.
    /// </param>
    /// <param name="replaceMark">Maps each mark; <see langword="null"/> removes it.</param>
    /// <param name="replaceRange">Maps each wrapper; <see langword="null"/> removes it.</param>
    internal void RewriteAnchors(
        Func<InlineObject, InlineObject?>? replaceObject = null,
        Func<InlineMark, InlineMark?>? replaceMark = null,
        Func<InlineRange, InlineRange?>? replaceRange = null)
    {
        if (replaceObject is not null && _objects is not null)
        {
            for (int i = _objects.Count - 1; i >= 0; i--)
            {
                InlineObject? next = replaceObject(_objects[i].Object);
                if (next is null)
                {
                    int offset = _objects[i].Offset;
                    _objects.RemoveAt(i);
                    RemoveText(offset, 1);
                }
                else if (!ReferenceEquals(next, _objects[i].Object))
                {
                    _objects[i] = new AnchoredObject { Offset = _objects[i].Offset, Object = next };
                    Anchor(next);
                }
            }
        }

        if (replaceMark is not null && _marks is not null)
        {
            for (int i = _marks.Count - 1; i >= 0; i--)
            {
                InlineMark? next = replaceMark(_marks[i].Mark);
                if (next is null)
                    _marks.RemoveAt(i);
                else if (!ReferenceEquals(next, _marks[i].Mark))
                    _marks[i] = new AnchoredMark { Offset = _marks[i].Offset, Mark = next };
            }
        }

        if (replaceRange is not null && _ranges is not null)
        {
            for (int i = _ranges.Count - 1; i >= 0; i--)
            {
                InlineRange? next = replaceRange(_ranges[i].Range);
                if (next is null)
                    _ranges.RemoveAt(i);
                else if (!ReferenceEquals(next, _ranges[i].Range))
                    _ranges[i] = new AnchoredRange { Start = _ranges[i].Start, Length = _ranges[i].Length, Range = next };
            }
        }
    }

    /// <summary>Rewrites the formatting of every run and of the paragraph mark.</summary>
    /// <param name="map">What each format becomes.</param>
    internal void RewriteRunFormats(Func<RunFormat, RunFormat> map)
    {
        for (int i = 0; i < _runs.Count; i++)
        {
            RunSpan run = _runs[i];
            run.Format = map(run.Format);
            _runs[i] = run;
        }

        MarkFormat = map(MarkFormat);
    }

    /// <summary>
    /// Replaces a stretch of text. Wrappers that covered the whole stretch keep covering the
    /// replacement, which is what makes a hyperlink or a content control survive having its
    /// text swapped out.
    /// </summary>
    /// <param name="start">Offset of the first character to replace.</param>
    /// <param name="count">How many characters to replace.</param>
    /// <param name="text">The replacement text.</param>
    /// <param name="format">Formatting of the replacement, or <see langword="null"/> to take it from the surrounding run.</param>
    public Paragraph ReplaceText(int start, int count, ReadOnlySpan<char> text, RunFormat? format = null)
    {
        start = Math.Clamp(start, 0, _length);
        count = Math.Clamp(count, 0, _length - start);
        if (count == 0 && text.IsEmpty)
            return this;

        // While changes are being recorded, an edit leaves a mark instead of rewriting the
        // text: what goes away stays where it is under a deletion.
        if (Document?.ActiveTracking is { } tracking)
        {
            Editing.RevisionRecorder.Replace(this, tracking, start, count, text, format);
            return this;
        }

        ReplaceTextCore(start, count, text, format);
        return this;
    }

    /// <summary>Replaces a stretch of text outright, whether or not changes are being recorded.</summary>
    internal void ReplaceTextCore(int start, int count, scoped ReadOnlySpan<char> text, RunFormat? format)
    {
        start = Math.Clamp(start, 0, _length);
        count = Math.Clamp(count, 0, _length - start);
        if (count == 0 && text.IsEmpty)
            return;

        int end = start + count;
        int delta = text.Length - count;

        format ??= FormatAt(start);
        RunKind kind = KindAt(start);
        string? attributes = AttributesAt(start);

        RebuildRuns(start, end, delta, text.Length, format, kind, attributes);
        ShiftObjects(start, end, delta);
        ShiftMarks(start, end, delta, text.Length);
        ShiftRanges(start, end, delta, count, text.Length);
        SpliceBuffer(start, end, text);
        MergeRuns();
    }

    /// <summary>
    /// Applies a formatting change to a stretch of text, splitting runs at its edges.
    /// </summary>
    /// <param name="start">Offset of the first character.</param>
    /// <param name="length">How many characters to reformat.</param>
    /// <param name="transform">Produces the new formatting from the old.</param>
    public Paragraph ApplyFormat(int start, int length, Func<RunFormat, RunFormat> transform)
    {
        ArgumentNullException.ThrowIfNull(transform);
        start = Math.Clamp(start, 0, _length);
        length = Math.Clamp(length, 0, _length - start);
        if (length == 0)
            return this;

        SplitRunAt(start);
        SplitRunAt(start + length);

        RevisionTracking? tracking = Document?.ActiveTracking;
        Span<RunSpan> runs = CollectionsMarshal.AsSpan(_runs);
        for (int i = 0; i < runs.Length; i++)
        {
            if (runs[i].Start < start || runs[i].End > start + length)
                continue;

            RunFormat original = runs[i].Format;
            RunFormat changed = transform(original);

            // A recorded formatting change carries what the formatting was, so that rejecting
            // it can put the run back rather than guess at what it looked like.
            runs[i].Format = tracking is null || changed == original
                ? changed
                : changed with { ChangeXml = Editing.RevisionRecorder.FormatChange(original, tracking) };
        }

        MergeRuns();
        return this;
    }

    /// <summary>
    /// Retags a stretch so that its text writes as removed, or as ordinary text again. The
    /// tag is what decides between <c>w:t</c> and <c>w:delText</c>, and a run inside a
    /// <c>w:del</c> that still says <c>w:t</c> is a file Word refuses.
    /// </summary>
    /// <param name="start">Offset of the first character.</param>
    /// <param name="length">How many characters to retag.</param>
    /// <param name="deleted">Whether the stretch reads as removed.</param>
    internal void SetDeletedRuns(int start, int length, bool deleted)
    {
        start = Math.Clamp(start, 0, _length);
        length = Math.Clamp(length, 0, _length - start);
        if (length == 0)
            return;

        SplitRunAt(start);
        SplitRunAt(start + length);

        Span<RunSpan> runs = CollectionsMarshal.AsSpan(_runs);
        for (int i = 0; i < runs.Length; i++)
        {
            if (runs[i].Start < start || runs[i].End > start + length)
                continue;

            runs[i].Kind = (runs[i].Kind, deleted) switch
            {
                (RunKind.Text, true) => RunKind.Deleted,
                (RunKind.FieldInstruction, true) => RunKind.DeletedFieldInstruction,
                (RunKind.Deleted, false) => RunKind.Text,
                (RunKind.DeletedFieldInstruction, false) => RunKind.FieldInstruction,
                _ => runs[i].Kind,
            };
        }

        MergeRuns();
    }

    /// <summary>Whether the character at an offset is already recorded as removed.</summary>
    /// <param name="offset">Offset to look at.</param>
    internal bool IsDeletedAt(int offset) =>
        KindAt(offset) is RunKind.Deleted or RunKind.DeletedFieldInstruction;

    /// <summary>Applies a formatting change to the whole paragraph, including the paragraph mark.</summary>
    /// <param name="transform">Produces the new formatting from the old.</param>
    public Paragraph ApplyFormat(Func<RunFormat, RunFormat> transform)
    {
        ArgumentNullException.ThrowIfNull(transform);
        Span<RunSpan> runs = CollectionsMarshal.AsSpan(_runs);
        for (int i = 0; i < runs.Length; i++)
            runs[i].Format = transform(runs[i].Format);
        MarkFormat = transform(MarkFormat);
        return this;
    }

    /// <summary>Splits the run that straddles an offset so that a run boundary lands exactly there.</summary>
    /// <param name="offset">Where a boundary is needed.</param>
    internal void SplitRunAt(int offset)
    {
        if (offset <= 0 || offset >= _length)
            return;

        for (int i = 0; i < _runs.Count; i++)
        {
            RunSpan run = _runs[i];
            if (run.Start >= offset)
                return;
            if (run.End <= offset)
                continue;

            _runs[i] = run with { Length = offset - run.Start };
            _runs.Insert(i + 1, run with { Start = offset, Length = run.End - offset });
            return;
        }
    }

    /// <summary>Formatting of the run covering an offset, or the nearest one.</summary>
    internal RunFormat FormatAt(int offset) => RunAt(offset)?.Format ?? RunFormat.Default;

    private RunKind KindAt(int offset) => RunAt(offset)?.Kind ?? RunKind.Text;

    private string? AttributesAt(int offset) => RunAt(offset)?.Attributes;

    private RunSpan? RunAt(int offset)
    {
        if (_runs.Count == 0)
            return null;

        foreach (RunSpan run in _runs)
        {
            if (offset < run.End)
                return run;
        }

        return _runs[^1];
    }

    private void RebuildRuns(int start, int end, int delta, int insertedLength, RunFormat format, RunKind kind, string? attributes)
    {
        var rebuilt = new List<RunSpan>(_runs.Count + 2);
        bool inserted = insertedLength == 0;

        foreach (RunSpan run in _runs)
        {
            int leftEnd = Math.Min(run.End, start);
            if (leftEnd > run.Start)
                rebuilt.Add(run with { Length = leftEnd - run.Start });

            int rightStart = Math.Max(run.Start, end);
            if (run.End > rightStart)
            {
                if (!inserted)
                {
                    rebuilt.Add(new RunSpan { Start = start, Length = insertedLength, Format = format, Kind = kind, Attributes = attributes });
                    inserted = true;
                }

                rebuilt.Add(run with { Start = rightStart + delta, Length = run.End - rightStart });
            }
        }

        if (!inserted)
            rebuilt.Add(new RunSpan { Start = start, Length = insertedLength, Format = format, Kind = kind, Attributes = attributes });

        _runs.Clear();
        _runs.AddRange(rebuilt);
    }

    private void ShiftObjects(int start, int end, int delta)
    {
        if (_objects is null)
            return;

        _objects.RemoveAll(o => o.Offset >= start && o.Offset < end);
        Span<AnchoredObject> objects = CollectionsMarshal.AsSpan(_objects);
        for (int i = 0; i < objects.Length; i++)
        {
            if (objects[i].Offset >= end)
                objects[i].Offset += delta;
        }

        if (_objects.Count == 0)
            _objects = null;
    }

    private void ShiftMarks(int start, int end, int delta, int insertedLength)
    {
        if (_marks is null)
            return;

        Span<AnchoredMark> marks = CollectionsMarshal.AsSpan(_marks);
        for (int i = 0; i < marks.Length; i++)
        {
            int offset = marks[i].Offset;
            if (offset <= start)
                continue;
            marks[i].Offset = offset >= end
                ? offset + delta
                : marks[i].Mark is BookmarkEnd or CommentRangeEnd ? start + insertedLength : start;
        }
    }

    private void ShiftRanges(int start, int end, int delta, int removedLength, int insertedLength)
    {
        if (_ranges is null)
            return;

        Span<AnchoredRange> ranges = CollectionsMarshal.AsSpan(_ranges);
        for (int i = 0; i < ranges.Length; i++)
        {
            int rangeStart = MapStart(ranges[i].Start);
            int rangeEnd = MapEnd(ranges[i].End);
            ranges[i].Start = rangeStart;
            ranges[i].Length = Math.Max(0, rangeEnd - rangeStart);
        }

        return;

        // A range keeps its outer edges: content spliced at its leading edge lands inside it,
        // content appended at its trailing edge lands outside. Replacing exactly the range
        // therefore leaves the wrapper covering the replacement.
        int MapStart(int position) =>
            position <= start ? position :
            position >= end ? position + delta : start;

        int MapEnd(int position) =>
            position > start && position < end ? start + insertedLength :
            position <= start ? position :
            removedLength == 0 && position == start ? position : position + delta;
    }

    private void SpliceBuffer(int start, int end, scoped ReadOnlySpan<char> text)
    {
        int tail = _length - end;
        int newLength = start + text.Length + tail;
        EnsureCapacity(newLength);

        if (tail > 0)
            _buffer.AsSpan(end, tail).CopyTo(_buffer.AsSpan(start + text.Length));
        text.CopyTo(_buffer.AsSpan(start));

        _length = newLength;
        _cachedText = null;
    }

    private void MergeRuns()
    {
        for (int i = _runs.Count - 1; i >= 0; i--)
        {
            if (_runs[i].Length <= 0)
            {
                _runs.RemoveAt(i);
                continue;
            }

            if (i == 0)
                continue;

            RunSpan previous = _runs[i - 1];
            RunSpan current = _runs[i];
            if (previous.End == current.Start && previous.Kind == current.Kind &&
                ReferenceEquals(previous.Format, current.Format) && previous.Attributes == current.Attributes)
            {
                _runs[i - 1] = previous with { Length = previous.Length + current.Length };
                _runs.RemoveAt(i);
            }
        }
    }
}
