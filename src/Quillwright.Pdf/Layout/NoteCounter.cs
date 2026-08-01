using Quillwright.Model;

namespace Quillwright.Pdf.Layout;

/// <summary>A note as it is about to be printed: its number, its body and how it wants to be shown.</summary>
/// <param name="Number">The mark that stands in the text and again at the head of the note.</param>
/// <param name="Body">The note itself, or <see langword="null"/> when the document has lost it.</param>
/// <param name="Settings">Where the note goes and how it is numbered.</param>
/// <param name="IsEndnote">Whether it is collected at the end rather than printed on the page.</param>
internal readonly record struct NoteMark(string Number, Note? Body, NoteProperties Settings, bool IsEndnote);

/// <summary>
/// Numbers the notes of a document as their references go by.
/// </summary>
/// <remarks>
/// Like a list counter, a note number is not stored anywhere: it is the position of the reference
/// in the document. So it is counted here, in document order, and the same rehearsal rule applies
/// — measuring a table's columns lays its cells out several times over, and a note inside one must
/// not be counted those extra times.
/// </remarks>
internal sealed class NoteCounter
{
    private readonly WordDocument _document;
    private readonly PdfExportDiagnostics _diagnostics;

    private Section? _section;
    private int _footnotes;
    private int _endnotes;

    internal NoteCounter(WordDocument document, PdfExportDiagnostics diagnostics)
    {
        _document = document;
        _diagnostics = diagnostics;
    }

    /// <summary>Numbers the note a reference points at, and moves the count on.</summary>
    /// <param name="reference">The reference met in the text.</param>
    /// <param name="section">The section the reference sits in.</param>
    public NoteMark? Next(NoteReference reference, Section section) => Resolve(reference, section, advance: true);

    /// <summary>The number a reference would take, without moving anything.</summary>
    /// <param name="reference">The reference met in the text.</param>
    /// <param name="section">The section the reference sits in.</param>
    public NoteMark? Peek(NoteReference reference, Section section) => Resolve(reference, section, advance: false);

    private NoteMark? Resolve(NoteReference reference, Section section, bool advance)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(section);

        NoteProperties settings = Settings(section, reference.IsEndnote);

        // A note whose mark the author wrote out is not numbered; the mark is in the text already.
        if (reference.CustomMark)
            return new NoteMark(string.Empty, Body(reference), settings, reference.IsEndnote);

        if (settings.Restart == NoteRestart.EachPage)
        {
            _diagnostics.Add(
                PdfExportWarningKind.LayoutApproximated,
                "Notes numbered afresh on every page are numbered straight through instead, because " +
                "which page a note lands on is decided by how tall its own number makes the text.",
                "note-restart");
        }

        int next = Advance(section, settings, reference.IsEndnote, advance);
        return new NoteMark(
            NumberFormatter.Format(next, settings.NumberFormat), Body(reference), settings, reference.IsEndnote);
    }

    private int Advance(Section section, NoteProperties settings, bool endnote, bool advance)
    {
        bool restarting = settings.Restart == NoteRestart.EachSection && !ReferenceEquals(_section, section);
        int counted = endnote ? _endnotes : _footnotes;
        int next = restarting || counted == 0 ? settings.Start : counted + 1;

        if (!advance)
            return next;

        _section = section;
        if (endnote)
            _endnotes = next;
        else
            _footnotes = next;

        return next;
    }

    /// <summary>
    /// The settings in force: the section's own when it states them, and the document's otherwise.
    /// </summary>
    private NoteProperties Settings(Section section, bool endnote) => endnote
        ? section.Properties.EndnoteProperties ?? _document.Settings.Endnotes
        : section.Properties.FootnoteProperties ?? _document.Settings.Footnotes;

    /// <summary>The note a reference points at, skipping the separators the part opens with.</summary>
    private Note? Body(NoteReference reference)
    {
        IReadOnlyList<Note> notes = reference.IsEndnote ? _document.Endnotes : _document.Footnotes;

        foreach (Note note in notes)
        {
            if (note.Kind == NoteKind.Normal && note.Id == reference.Id)
                return note;
        }

        return null;
    }
}
