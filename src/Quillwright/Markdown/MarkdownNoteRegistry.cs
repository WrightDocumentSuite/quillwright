using Quillwright.Model;

namespace Quillwright.Markdown;

/// <summary>A referenced footnote or endnote waiting for its definition.</summary>
internal sealed class MarkdownNoteEntry
{
    public required string Label { get; init; }

    public required int Number { get; init; }

    public required Note Body { get; init; }

    public required bool IsEndnote { get; init; }
}

/// <summary>Collects notes lazily in order of their first visible reference.</summary>
internal sealed class MarkdownNoteRegistry
{
    private readonly WordDocument _document;
    private readonly MarkdownExportDiagnostics _diagnostics;
    private readonly Dictionary<(bool Endnote, int Id), MarkdownNoteEntry> _byReference = [];
    private readonly List<MarkdownNoteEntry> _entries = [];
    private readonly HashSet<string> _labels = new(StringComparer.Ordinal);

    public MarkdownNoteRegistry(WordDocument document, MarkdownExportDiagnostics diagnostics)
    {
        _document = document;
        _diagnostics = diagnostics;
    }

    public IReadOnlyList<MarkdownNoteEntry> Entries => _entries;

    public MarkdownNoteEntry? Add(NoteReference reference)
    {
        var key = (reference.IsEndnote, reference.Id);
        if (_byReference.TryGetValue(key, out MarkdownNoteEntry? existing))
            return existing;

        IReadOnlyList<Note> notes = reference.IsEndnote ? _document.Endnotes : _document.Footnotes;
        Note? body = notes.FirstOrDefault(note => note.Kind == NoteKind.Normal && note.Id == reference.Id);
        if (body is null)
        {
            _diagnostics.Add(
                MarkdownExportWarningKind.ContentSkipped,
                "A note reference points to a note body that is not present.",
                reference.IsEndnote ? "missing-endnote" : "missing-footnote");
            return null;
        }

        string prefix = reference.IsEndnote ? "en" : "fn";
        string basis = $"{prefix}-{Math.Abs((long)reference.Id)}";
        string label = basis;
        for (int suffix = 2; !_labels.Add(label); suffix++)
            label = $"{basis}-{suffix}";

        var entry = new MarkdownNoteEntry
        {
            Label = label,
            Number = _entries.Count + 1,
            Body = body,
            IsEndnote = reference.IsEndnote,
        };

        _byReference.Add(key, entry);
        _entries.Add(entry);

        if (reference.CustomMark)
        {
            _diagnostics.Add(
                MarkdownExportWarningKind.StructureApproximated,
                "A custom note mark is represented by the Markdown viewer's own note number.",
                "custom-note-mark");
        }

        return entry;
    }
}
