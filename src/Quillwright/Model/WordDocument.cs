using Quillwright.Diagnostics;
using Quillwright.Formats;
using Quillwright.IO;
using Quillwright.Styles;
using Quillwright.Vba;

namespace Quillwright.Model;

/// <summary>
/// A Word document in memory: sections of blocks, the styles and numbering they refer to,
/// and the notes, comments and images they point at.
/// </summary>
/// <remarks>
/// Loading reads the package to the end and closes it, and saving writes a new one, so there
/// is never an open file handle between calls. Everything the model does not represent is
/// held aside while the document is open and put back on save.
/// </remarks>
public sealed partial class WordDocument
{
    private readonly List<Comment> _comments = [];
    private readonly List<Person> _people = [];
    private readonly List<Note> _footnotes = [];
    private readonly List<Note> _endnotes = [];
    private readonly List<HeaderFooter> _headersAndFooters = [];
    private readonly List<DocumentWarning> _warnings = [];
    private readonly List<EmbeddedObject> _embeddedObjects = [];
    private readonly List<Chart> _charts = [];
    private readonly List<DigitalSignature> _signatures = [];
    private readonly List<WebExtension> _webExtensions = [];
    private StyleResolver? _resolver;

    private WordDocument()
    {
        Sections = new SectionCollection(this);

        // An abstract numbering may defer to a numbering style rather than declare its own
        // levels (§17.9.21), and only the document knows what that style is.
        Numbering.Owner = this;
    }

    /// <summary>The sections of the document, in order. There is always at least one.</summary>
    public SectionCollection Sections { get; }

    /// <summary>The style catalogue.</summary>
    public StyleSheet Styles { get; internal set; } = new();

    /// <summary>
    /// The colour scheme of the document's theme, or <see langword="null"/> when the package
    /// has no theme part. A colour that names a theme slot has a name and no value until this
    /// resolves it; see <see cref="ResolveColor"/>.
    /// </summary>
    public DocumentTheme? Theme { get; internal set; }

    /// <summary>
    /// Computes the formatting that actually applies to a paragraph or a run, after document
    /// defaults, table styles, numbering and the style chain have been layered together.
    /// </summary>
    public StyleResolver Resolver => _resolver ??= new StyleResolver(this);

    /// <summary>The list definitions and instances.</summary>
    public NumberingDefinitions Numbering { get; } = new();

    /// <summary>The application settings of the document.</summary>
    public DocumentSettings Settings { get; } = new();

    /// <summary>
    /// The tracked-change session edits are being recorded into, or <see langword="null"/>
    /// when they are applied outright. Opened by <c>document.TrackChanges(author)</c>.
    /// </summary>
    public RevisionTracking? ActiveTracking { get; internal set; }

    /// <summary>The core properties: title, author, dates.</summary>
    public DocumentProperties Properties { get; internal set; } = new();

    /// <summary>The application properties: which program wrote the document, and its statistics.</summary>
    public ExtendedProperties ApplicationProperties { get; } = new();

    /// <summary>The custom properties, the free-form metadata a document management system keeps.</summary>
    public CustomPropertyCollection CustomProperties { get; } = new();

    /// <summary>
    /// The objects embedded in the document: spreadsheets, slides, and the plain files someone
    /// attached.
    /// </summary>
    /// <remarks>
    /// Read only, as macros are. The parts holding them are copied through untouched on save,
    /// so what is read here is what a saved file carries.
    /// </remarks>
    public IReadOnlyList<EmbeddedObject> EmbeddedObjects => _embeddedObjects;

    /// <summary>The charts the document draws, with the numbers each one caches.</summary>
    /// <remarks>
    /// The chart parts are copied through untouched on save; <see cref="SetChartData"/> is the
    /// one thing that changes one, and it changes only the data.
    /// </remarks>
    public IReadOnlyList<Chart> Charts => _charts;

    /// <summary>
    /// The digital signatures over the package, in the order the signature origin part lists
    /// them (ECMA-376 part 2, clause 10).
    /// </summary>
    /// <remarks>
    /// Read only: signature parts are copied through untouched on save. What each signature
    /// covers is checked against the parts as they arrived; see
    /// <see cref="DigitalSignature"/> for exactly how far that goes.
    /// </remarks>
    public IReadOnlyList<DigitalSignature> Signatures => _signatures;

    /// <summary>The web extensions the document carries ([MS-OWEXML]).</summary>
    /// <remarks>
    /// Read and preserve: the <c>taskpanes.xml</c> and <c>webextensionN.xml</c> parts are copied
    /// through untouched on save, and this is a typed view over them. Nothing here authors an
    /// extension. The add-in's own manifest ([MS-OWEMXML]) is a separate file that lives in a
    /// catalogue rather than in the document, so it cannot be reached from a package and is not
    /// part of this model; <see cref="IO.OfficeAddInManifestReader"/> reads one on its own.
    /// </remarks>
    public IReadOnlyList<WebExtension> WebExtensions => _webExtensions;

    /// <summary>The images the document uses.</summary>
    public MediaCollection Media { get; } = new();

    /// <summary>The comments, in the order they appear in the comments part.</summary>
    public IReadOnlyList<Comment> Comments => _comments;

    /// <summary>
    /// Who the author names on comments and revisions belong to ([MS-DOCX] 2.5.3.4). Empty for
    /// a document that carries no <c>people.xml</c>.
    /// </summary>
    public IReadOnlyList<Person> People => _people;

    /// <summary>The footnotes, including the separator entries Word keeps at the top.</summary>
    public IReadOnlyList<Note> Footnotes => _footnotes;

    /// <summary>The endnotes, including the separator entries Word keeps at the top.</summary>
    public IReadOnlyList<Note> Endnotes => _endnotes;

    /// <summary>Recoverable problems found while loading.</summary>
    public IReadOnlyList<DocumentWarning> LoadDiagnostics => _warnings;

    /// <summary>Whether the document carries a VBA project and must be saved as <c>.docm</c>.</summary>
    public bool IsMacroEnabled =>
        Macros is not null ||
        Preserved?.MainContentType is DocxSchema.ContentTypeMacroDocument or DocxSchema.ContentTypeMacroTemplate;

    /// <summary>
    /// The VBA project the document was loaded with, or <see langword="null"/> when it has none.
    /// </summary>
    /// <remarks>
    /// Read only: the project is decoded for inspection, never rebuilt. Saving copies the
    /// original bytes through unchanged, so what is read here is what a saved file will run.
    /// </remarks>
    public VbaProject? Macros { get; internal set; }

    /// <summary>Every block of every section, in document order.</summary>
    public IEnumerable<Block> Blocks => Sections.SelectMany(static section => section.Blocks);

    /// <summary>Every paragraph of the body, in document order. Table cells are included.</summary>
    public IEnumerable<Paragraph> Paragraphs => Blocks.SelectMany(EnumerateParagraphs);

    /// <summary>
    /// Every block container in the document, including headers, footers, notes, comments and
    /// table cells. Used by operations that must reach the whole document, such as replace.
    /// </summary>
    public IEnumerable<BlockContainer> AllContainers
    {
        get
        {
            foreach (Section section in Sections)
            {
                foreach (BlockContainer container in Descend(section))
                    yield return container;
            }

            foreach (BlockContainer part in _headersAndFooters.Concat<BlockContainer>(_footnotes).Concat(_endnotes).Concat(_comments))
            {
                foreach (BlockContainer container in Descend(part))
                    yield return container;
            }
        }
    }

    internal PreservedPackage? Preserved { get; set; }

    /// <summary>
    /// The start tag of <c>w:document</c> as it was read, namespace declarations included.
    /// Preserved markup inside the body refers to prefixes declared there.
    /// </summary>
    internal string? RootAttributes { get; set; }

    /// <summary>The page background element, kept verbatim (<c>w:background</c>).</summary>
    internal string? BackgroundXml { get; set; }

    /// <summary>Root start tags of the note and comment parts, keyed by their root element name.</summary>
    internal Dictionary<string, string?> PartRoots { get; } = new(StringComparer.Ordinal);

    internal List<HeaderFooter> HeaderFooters => _headersAndFooters;

    internal List<Comment> CommentList => _comments;

    internal List<Person> PeopleList => _people;

    internal List<Note> FootnoteList => _footnotes;

    internal List<Note> EndnoteList => _endnotes;

    internal List<DocumentWarning> WarningList => _warnings;

    internal List<EmbeddedObject> EmbeddedObjectList => _embeddedObjects;

    internal List<Chart> ChartList => _charts;

    internal List<DigitalSignature> SignatureList => _signatures;

    internal List<WebExtension> WebExtensionList => _webExtensions;

    /// <summary>Creates an empty document with one section, A4 pages and the default styles.</summary>
    public static WordDocument Create()
    {
        var document = new WordDocument { Styles = StyleSheet.CreateDefault() };
        document.Sections.Add(new Section());
        return document;
    }

    /// <summary>Creates a document with no sections, for the loader to fill.</summary>
    internal static WordDocument CreateEmpty() => new();

    /// <summary>Reads a document from a file.</summary>
    /// <param name="path">Path to the <c>.docx</c>, <c>.docm</c> or <c>.dotx</c> file.</param>
    /// <param name="options">Controls fidelity and diagnostics.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public static async ValueTask<WordDocument> LoadAsync(string path, LoadOptions? options = null, CancellationToken cancellationToken = default)
    {
        FileStream stream = new(path, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
        });

        await using (stream.ConfigureAwait(false))
        {
            return await LoadAsync(stream, options, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Reads a document from a stream. The stream is left open.</summary>
    /// <param name="stream">Stream positioned at the start of the package.</param>
    /// <param name="options">Controls fidelity and diagnostics.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public static ValueTask<WordDocument> LoadAsync(Stream stream, LoadOptions? options = null, CancellationToken cancellationToken = default) =>
        DocxLoader.LoadAsync(stream, options ?? LoadOptions.Default, cancellationToken);

    /// <summary>Writes the document to a file, replacing it if it exists.</summary>
    /// <param name="path">Destination path.</param>
    /// <param name="options">Controls what is written.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public async ValueTask SaveAsync(string path, SaveOptions? options = null, CancellationToken cancellationToken = default)
    {
        FileStream stream = new(path, new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
        });

        await using (stream.ConfigureAwait(false))
        {
            await SaveAsync(stream, options, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Writes the document to a stream. The stream is left open.</summary>
    /// <param name="stream">Destination stream.</param>
    /// <param name="options">Controls what is written.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public ValueTask SaveAsync(Stream stream, SaveOptions? options = null, CancellationToken cancellationToken = default)
    {
        SaveOptions settings = options ?? SaveOptions.Default;
        return settings.Password is { Length: > 0 }
            ? SaveEncryptedAsync(stream, settings, cancellationToken)
            : DocxSaver.SaveAsync(this, stream, settings, cancellationToken);
    }

    /// <summary>
    /// Writes the package into memory and locks it away in a compound file. Encryption is not
    /// something the saver can do as it goes: the whole package has to exist before it can be
    /// hashed and chained.
    /// </summary>
    private async ValueTask SaveEncryptedAsync(Stream stream, SaveOptions options, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await DocxSaver.SaveAsync(this, buffer, options, cancellationToken).ConfigureAwait(false);

        byte[] locked = OfficeEncryptionWriter.Encrypt(buffer.ToArray(), options.Password!);
        await stream.WriteAsync(locked, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The value a colour actually shows as: itself when it is literal, and the theme slot it
    /// names — tinted or shaded as the colour asks — when it is not.
    /// </summary>
    /// <param name="color">The colour to resolve.</param>
    /// <returns>
    /// The packed <c>0xRRGGBB</c> value, or <see langword="null"/> for the automatic colour and
    /// for a theme colour this document's theme does not define.
    /// </returns>
    /// <remarks>
    /// A theme colour is stored as a name so that changing the theme recolours the document,
    /// which means the value is not in the run at all: it is in the theme part, possibly
    /// through a mapping in the settings, and then lightened or darkened. Word writes the
    /// answer it computed into the same element as a cache, so a colour read from a file
    /// usually has both — but one built here, or one whose theme has been swapped, has only
    /// the name.
    /// </remarks>
    public uint? ResolveColor(Primitives.WordColor color) => color.Kind switch
    {
        Primitives.ColorKind.Rgb => color.Rgb,
        Primitives.ColorKind.Theme => Theme?.Resolve(color),
        _ => null,
    };

    /// <summary>The text of the body, with one line per paragraph and tabs between table cells.</summary>
    public string GetText()
    {
        var builder = new System.Text.StringBuilder();
        foreach (Section section in Sections)
        {
            foreach (Block block in section.Blocks)
            {
                if (builder.Length > 0)
                    builder.Append('\n');
                builder.Append(block.GetText());
            }
        }

        return builder.ToString();
    }

    /// <summary>Appends the content of another document to the end of this one.</summary>
    /// <param name="source">The document to copy from; it is not changed.</param>
    /// <param name="options">How the content arrives, or <see langword="null"/> for the defaults.</param>
    /// <returns>What could not be carried, one warning per thing left behind.</returns>
    /// <remarks>
    /// <para>
    /// The content is copied, never shared, and everything it leans on comes along: styles
    /// with their <c>basedOn</c> chains, numbering with fresh instance ids, images, footnotes,
    /// endnotes, comments with their threading, hyperlinks rebound to the target, and bookmark
    /// ids shifted clear of this document's. A style this document already defines wins over
    /// the source's definition of the same name, which is what Word does when pasting.
    /// </para>
    /// <para>
    /// What cannot come along is what lives in the source package rather than the model: a
    /// chart part, an OLE object, verbatim markup that points at a part by relationship id.
    /// Each is left out with a warning naming it, because carrying a dangling reference would
    /// make Word repair the file.
    /// </para>
    /// </remarks>
    public IReadOnlyList<Diagnostics.DocumentWarning> Append(WordDocument source, DocumentAppendOptions? options = null) =>
        Editing.DocumentMerger.Append(this, source, options ?? DocumentAppendOptions.Default);

    /// <summary>Replaces the data a chart draws, leaving how it looks alone.</summary>
    /// <param name="chart">The chart, from <see cref="Charts"/>.</param>
    /// <param name="series">One entry per series the chart draws, in the order it draws them.</param>
    /// <returns>The chart re-read from the rewritten part, which replaces it in <see cref="Charts"/>.</returns>
    /// <exception cref="ArgumentException">
    /// The chart is not this document's, or the number of series given differs from the number
    /// the chart draws — adding or removing a series changes the chart's structure, which stays
    /// the author's job.
    /// </exception>
    /// <exception cref="NotSupportedException">
    /// The chart lives inside a legacy embedded object rather than a chart part.
    /// </exception>
    /// <remarks>
    /// The new names, categories, values and bubble sizes are written into the part as
    /// literals, so after this the chart's data lives in the chart itself: the formula that
    /// pointed into the embedded workbook is gone from what was rewritten, and an "Edit Data"
    /// in Word opens the workbook with its old numbers. A <see langword="null"/> series name
    /// keeps the existing one; an empty category list removes the categories, which a chart
    /// draws as 1, 2, 3; a <see langword="null"/> value is a gap, drawn as one.
    /// </remarks>
    public Chart SetChartData(Chart chart, IReadOnlyList<ChartSeries> series)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentNullException.ThrowIfNull(series);

        int index = _charts.IndexOf(chart);
        if (index < 0)
            throw new ArgumentException("The chart is not one of this document's.", nameof(chart));

        if (Preserved is null || !Preserved.Parts.TryGetValue(chart.Location, out byte[]? content))
        {
            throw new NotSupportedException(
                $"The chart at '{chart.Location}' is not a package part, so its data cannot be rewritten; " +
                "a chart inside a legacy embedded object only says what it is.");
        }

        byte[] rewritten = ChartDataWriter.Rewrite(content, series);
        Preserved.Parts[chart.Location] = rewritten;

        using var xml = System.Xml.XmlReader.Create(new MemoryStream(rewritten), Xml.XmlDefaults.ReaderSettings);
        Chart updated = ChartPartReader.Read(xml, chart.Location);
        _charts[index] = updated;
        return updated;
    }

    /// <summary>Adds a comment anchored to a stretch of a paragraph.</summary>
    /// <param name="paragraph">Paragraph the comment points at.</param>
    /// <param name="start">Offset of the first commented character.</param>
    /// <param name="length">How many characters the comment covers.</param>
    /// <param name="text">Text of the comment.</param>
    /// <param name="author">Who wrote it.</param>
    /// <param name="initials">The author's initials.</param>
    /// <remarks>
    /// The reference the reader clicks is a character of its own, placed at the end of the
    /// commented range, so offsets in the paragraph past that point move along by one. A
    /// caller adding several comments to one paragraph should work backwards, or take the
    /// offsets from the paragraph again after each call.
    /// </remarks>
    public Comment AddComment(Paragraph paragraph, int start, int length, string text, string? author = null, string? initials = null)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        int end = Math.Clamp(start + length, 0, paragraph.TextLength);
        return AddComment(paragraph, Math.Clamp(start, 0, end), paragraph, end, text, author, initials);
    }

    /// <summary>Adds a comment anchored to a stretch of text that runs across paragraphs.</summary>
    /// <param name="startParagraph">Paragraph the commented range opens in.</param>
    /// <param name="startOffset">Offset of the first commented character.</param>
    /// <param name="endParagraph">Paragraph the commented range closes in.</param>
    /// <param name="endOffset">Offset just past the last commented character.</param>
    /// <param name="text">Text of the comment.</param>
    /// <param name="author">Who wrote it.</param>
    /// <param name="initials">The author's initials.</param>
    /// <remarks>
    /// The opening mark goes into the first paragraph and the closing mark and the reference
    /// into the last, which is how both formats express a range that crosses a paragraph
    /// break. The two paragraphs are not required to be in the same section, table cell or
    /// story, but a range that runs backwards is meaningless and nothing checks for it.
    /// </remarks>
    public Comment AddComment(
        Paragraph startParagraph,
        int startOffset,
        Paragraph endParagraph,
        int endOffset,
        string text,
        string? author = null,
        string? initials = null)
    {
        ArgumentNullException.ThrowIfNull(startParagraph);
        ArgumentNullException.ThrowIfNull(endParagraph);

        Comment comment = NewComment(text, author, initials);
        Anchor(
            comment,
            startParagraph,
            Math.Clamp(startOffset, 0, startParagraph.TextLength),
            endParagraph,
            Math.Clamp(endOffset, 0, endParagraph.TextLength));
        _comments.Add(comment);
        return comment;
    }

    /// <summary>
    /// Creates a comment for a format reader that will materialize all imported anchors in one batch.
    /// </summary>
    internal Comment AddImportedComment(
        int id,
        string? author,
        string? initials)
    {
        var comment = new Comment(this)
        {
            Id = id,
            Author = author,
            Initials = initials,
        };
        Styles.GetOrAdd("CommentText");
        Styles.GetOrAdd("CommentReference", StyleKind.Character);
        _comments.Add(comment);
        return comment;
    }

    /// <summary>
    /// Adds a reply to a comment, over the same stretch of text the comment it answers covers.
    /// </summary>
    /// <param name="parent">The comment being replied to.</param>
    /// <param name="text">Text of the reply.</param>
    /// <param name="author">Who wrote it.</param>
    /// <param name="initials">The author's initials.</param>
    /// <exception cref="InvalidOperationException">The comment replied to is not anchored anywhere.</exception>
    /// <remarks>
    /// A reply is a comment like any other, joined to the one it answers by the threading part
    /// ([MS-DOCX] 2.5.3.1) and by covering the same range. Replying to a reply is allowed and
    /// is what Word itself writes for a conversation of more than two.
    /// </remarks>
    public Comment AddReply(Comment parent, string text, string? author = null, string? initials = null)
    {
        ArgumentNullException.ThrowIfNull(parent);
        if (FindAnchor(parent.Id) is not { } anchor)
        {
            throw new InvalidOperationException(
                $"Comment {parent.Id} is not anchored to any text, so a reply to it could not be anchored either.");
        }

        Comment reply = NewComment(text, author, initials);
        reply.ParentId = parent.Id;
        Anchor(reply, anchor.StartParagraph, anchor.StartOffset, anchor.EndParagraph, anchor.EndOffset);
        _comments.Add(reply);
        return reply;
    }

    /// <summary>Builds the balloon of a comment, without anchoring it to anything.</summary>
    /// <param name="text">Text of the comment.</param>
    /// <param name="author">Who wrote it.</param>
    /// <param name="initials">The author's initials.</param>
    private Comment NewComment(string text, string? author, string? initials)
    {
        var comment = new Comment(this)
        {
            Id = _comments.Count == 0 ? 1 : _comments.Max(static c => c.Id) + 1,
            Author = author,
            Initials = initials,
            Date = DateTimeOffset.Now,
        };

        comment.AddParagraph(text).Format = comment.Blocks.Paragraphs.First().Format with { StyleId = "CommentText" };
        Styles.GetOrAdd("CommentText");
        Styles.GetOrAdd("CommentReference", StyleKind.Character);
        return comment;
    }

    /// <summary>Puts the marks and the reference of a comment into the text.</summary>
    /// <param name="comment">The comment being anchored.</param>
    /// <param name="startParagraph">Paragraph the commented range opens in.</param>
    /// <param name="startOffset">Where it opens.</param>
    /// <param name="endParagraph">Paragraph the commented range closes in.</param>
    /// <param name="endOffset">Where it closes.</param>
    private static void Anchor(Comment comment, Paragraph startParagraph, int startOffset, Paragraph endParagraph, int endOffset)
    {
        // Each comment sharing a range keeps a reference of its own, and Word writes them in
        // the order the comments were made, so this one steps over any already sitting there.
        int end = endOffset;
        foreach ((int offset, InlineObject anchored) in endParagraph.Objects)
        {
            if (offset == end && anchored is CommentReference)
                end++;
        }

        startParagraph.AddMark(new CommentRangeStart { Id = comment.Id }, startOffset);

        // The reference belongs at the end of the commented range, not at the end of the
        // paragraph: it is what the reader clicks, and both formats tie the two together.
        endParagraph.InsertObject(end, new CommentReference { Id = comment.Id },
            RunFormat.Default with { StyleId = "CommentReference" });
        endParagraph.AddMark(new CommentRangeEnd { Id = comment.Id }, end);
    }

    /// <summary>
    /// Where a comment is attached, looked up from the marks in the text rather than held on
    /// the comment, because the text is where it actually lives and editing can move it.
    /// </summary>
    /// <param name="id">Identifier of the comment.</param>
    private CommentAnchor? FindAnchor(int id)
    {
        (Paragraph Paragraph, int Offset)? start = null;
        (Paragraph Paragraph, int Offset)? end = null;
        (Paragraph Paragraph, int Offset)? reference = null;

        foreach (BlockContainer container in AllContainers)
        {
            foreach (Paragraph paragraph in container.Blocks.Paragraphs)
            {
                foreach ((int offset, InlineMark mark) in paragraph.Marks)
                {
                    if (mark is CommentRangeStart from && from.Id == id)
                        start ??= (paragraph, offset);
                    else if (mark is CommentRangeEnd to && to.Id == id)
                        end ??= (paragraph, offset);
                }

                foreach ((int offset, InlineObject anchored) in paragraph.Objects)
                {
                    if (anchored is CommentReference point && point.Id == id)
                        reference ??= (paragraph, offset);
                }
            }
        }

        // A comment with no range of its own is still anchored, at the character the reader
        // clicks, and a reply to it goes in the same place.
        end ??= reference;
        start ??= end;
        return end is null || start is null
            ? null
            : new CommentAnchor(start.Value.Paragraph, start.Value.Offset, end.Value.Paragraph, end.Value.Offset);
    }

    /// <summary>Where in the text a comment is attached.</summary>
    /// <param name="StartParagraph">Paragraph the commented range opens in.</param>
    /// <param name="StartOffset">Where it opens.</param>
    /// <param name="EndParagraph">Paragraph the commented range closes in.</param>
    /// <param name="EndOffset">Where it closes.</param>
    private readonly record struct CommentAnchor(
        Paragraph StartParagraph, int StartOffset, Paragraph EndParagraph, int EndOffset);

    /// <summary>Adds a footnote and appends its reference to a paragraph.</summary>
    /// <param name="paragraph">Paragraph the reference goes into.</param>
    /// <param name="text">Text of the note.</param>
    public Note AddFootnote(Paragraph paragraph, string text) => AddNote(paragraph, text, isEndnote: false);

    /// <summary>Adds an endnote and appends its reference to a paragraph.</summary>
    /// <param name="paragraph">Paragraph the reference goes into.</param>
    /// <param name="text">Text of the note.</param>
    public Note AddEndnote(Paragraph paragraph, string text) => AddNote(paragraph, text, isEndnote: true);

    internal void RegisterHeaderFooter(HeaderFooter part) => _headersAndFooters.Add(part);

    internal void Warn(DocumentWarning warning, LoadOptions options)
    {
        _warnings.Add(warning);
        options.OnWarning?.Invoke(warning);
    }

    private Note AddNote(Paragraph paragraph, string text, bool isEndnote)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        List<Note> notes = isEndnote ? _endnotes : _footnotes;
        EnsureNoteSeparators(notes, isEndnote);

        var note = new Note(this, isEndnote)
        {
            Id = notes.Count == 0 ? 1 : notes.Max(static n => n.Id) + 1,
        };

        string styleId = isEndnote ? "EndnoteText" : "FootnoteText";
        string referenceStyle = isEndnote ? "EndnoteReference" : "FootnoteReference";
        Styles.GetOrAdd(styleId);
        Styles.GetOrAdd(referenceStyle, StyleKind.Character);

        Paragraph body = note.AddParagraph();
        body.Format = body.Format with { StyleId = styleId };
        body.AppendObject(new NoteNumberMark { IsEndnote = isEndnote },
            RunFormat.Default with { StyleId = referenceStyle });
        body.AppendText(" " + text, RunFormat.Default);

        notes.Add(note);
        paragraph.AppendObject(new NoteReference { IsEndnote = isEndnote, Id = note.Id },
            RunFormat.Default with { StyleId = referenceStyle });
        return note;
    }

    private void EnsureNoteSeparators(List<Note> notes, bool isEndnote)
    {
        if (notes.Count > 0)
            return;

        var separator = new Note(this, isEndnote) { Id = -1, Kind = NoteKind.Separator };
        separator.AddParagraph().AppendObject(new NoteSeparator());
        var continuation = new Note(this, isEndnote) { Id = 0, Kind = NoteKind.ContinuationSeparator };
        continuation.AddParagraph().AppendObject(new NoteSeparator { IsContinuation = true });
        notes.Add(separator);
        notes.Add(continuation);
    }

    private static IEnumerable<Paragraph> EnumerateParagraphs(Block block) => block switch
    {
        Paragraph paragraph => [paragraph],
        Table table => table.Rows
            .SelectMany(static row => row.Cells)
            .SelectMany(static cell => cell.Blocks)
            .SelectMany(EnumerateParagraphs),
        _ => [],
    };

    private static IEnumerable<BlockContainer> Descend(BlockContainer container)
    {
        yield return container;
        foreach (Block block in container.Blocks)
        {
            IEnumerable<BlockContainer> children = block switch
            {
                Table table => table.Rows.SelectMany(static row => row.Cells),

                // The branch of a compatibility block that this version reads is ordinary
                // content, so everything that reaches the document has to reach into it.
                AlternateContentBlock alternate => [alternate.Content],

                // A text box is a container anchored in the text, so anything that reaches the
                // whole document — replace, comments — has to reach inside it too.
                Paragraph paragraph => paragraph.Objects
                    .Select(static entry => entry.Object)
                    .OfType<Shape>()
                    .Select(static shape => shape.Content),
                _ => [],
            };

            foreach (BlockContainer child in children)
            {
                foreach (BlockContainer nested in Descend(child))
                    yield return nested;
            }
        }
    }
}
