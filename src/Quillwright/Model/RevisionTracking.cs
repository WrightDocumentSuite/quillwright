namespace Quillwright.Model;

/// <summary>
/// A session in which edits are recorded as tracked changes rather than applied outright
/// (ISO/IEC 29500-1 §17.13.5).
/// </summary>
/// <remarks>
/// <para>
/// While a session is open, every edit made through the ordinary editing API leaves a mark
/// instead of quietly rewriting the text: inserted text is wrapped in <c>w:ins</c>, deleted
/// text stays where it is under <c>w:del</c>, and a formatting change records what the
/// formatting was. Disposing the session stops recording and puts
/// <see cref="DocumentSettings.TrackRevisions"/> back the way it was, because that setting
/// describes the mode the document is in rather than what one tool did to it.
/// </para>
/// <para>
/// The session remembers the revisions it made, which is what lets it tell a caller undoing
/// their own insertion — where the text should simply go — from one deleting text that was
/// already in the document.
/// </para>
/// </remarks>
public sealed class RevisionTracking : IDisposable
{
    private readonly WordDocument _document;
    private readonly HashSet<Revision> _recorded = [];
    private readonly HashSet<Paragraph> _added = [];
    private readonly bool _wasTracking;
    private int _nextId;

    internal RevisionTracking(WordDocument document, string author, DateTimeOffset? date)
    {
        _document = document;
        Author = author;
        Date = date;
        _wasTracking = document.Settings.TrackRevisions;
        _nextId = HighestId(document) + 1;
    }

    /// <summary>Who the recorded changes are attributed to.</summary>
    public string Author { get; }

    /// <summary>When they are recorded as having been made.</summary>
    public DateTimeOffset? Date { get; }

    /// <summary>Whether this session is the one the document is currently recording into.</summary>
    public bool IsRecording => ReferenceEquals(_document.ActiveTracking, this);

    /// <summary>Stops recording and restores the document's own tracking setting.</summary>
    public void Dispose()
    {
        if (!IsRecording)
            return;

        _document.ActiveTracking = null;
        _document.Settings.TrackRevisions = _wasTracking;
    }

    /// <summary>The next free revision identifier in the document.</summary>
    internal int NextId() => _nextId++;

    /// <summary>Makes a revision belonging to this session.</summary>
    internal Revision Create(RevisionKind kind)
    {
        var revision = new Revision { Kind = kind, Id = NextId(), Author = Author, Date = Date };
        _recorded.Add(revision);
        return revision;
    }

    /// <summary>Whether a revision is one this session made.</summary>
    internal bool Recorded(Revision revision) => _recorded.Contains(revision);

    /// <summary>Remembers a paragraph this session added, mark and all.</summary>
    internal void Added(Paragraph paragraph) => _added.Add(paragraph);

    /// <summary>Whether a paragraph is one this session added.</summary>
    internal bool WasAdded(Paragraph paragraph) => _added.Contains(paragraph);

    /// <summary>
    /// The largest revision identifier already in the document, so that the ones this session
    /// mints cannot collide with the ones an earlier author left behind.
    /// </summary>
    private static int HighestId(WordDocument document)
    {
        int highest = 0;
        foreach (BlockContainer container in document.AllContainers)
        {
            foreach (Paragraph paragraph in container.Blocks.Paragraphs)
            {
                foreach ((_, _, InlineRange range) in paragraph.Ranges)
                {
                    if (range is Revision revision && revision.Id > highest)
                        highest = revision.Id;
                }
            }
        }

        return highest;
    }
}
