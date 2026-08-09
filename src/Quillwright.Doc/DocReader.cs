using System.Text;
using Quillwright.Diagnostics;
using Quillwright.IO;
using Quillwright.Model;
using Quillwright.Styles;
using Quillwright.Vba;

namespace Quillwright.Doc;

/// <summary>
/// Reads a Word 97-2003 binary document into the Quillwright model, so it can be inspected
/// or saved as <c>.docx</c>.
/// </summary>
/// <remarks>
/// <para>
/// The legacy format keeps the text in one long stream and everything about it somewhere
/// else: the character order comes from a piece table, the paragraph boundaries from a set
/// of formatting pages indexed by byte offset, and the formatting itself from packed
/// property lists. Reading is therefore a join across four structures rather than a walk of
/// a tree.
/// </para>
/// <para>
/// This is a reader, not a converter of everything: text, paragraph and character
/// formatting, style names, tables and the separate stories for footnotes, headers and
/// comments come across. Floating drawings, embedded objects and revision marks do not, and
/// a text box keeps its text but loses its box — each of those puts a
/// <see cref="Quillwright.Diagnostics.DocumentWarning"/> in
/// <see cref="WordDocument.LoadDiagnostics"/> rather than going quietly.
/// </para>
/// <para>
/// A file this reader should not answer for at all is refused instead: one older than Word 97
/// with a <see cref="DocFormatException"/>, an encrypted one with an
/// <see cref="Quillwright.Diagnostics.EncryptedDocumentException"/>.
/// </para>
/// </remarks>
public static class DocReader
{
    /// <summary>Storage a legacy document keeps its VBA project in.</summary>
    private const string MacroStorage = "Macros";

    static DocReader() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>Reads a legacy document from a file.</summary>
    /// <param name="path">Path to the <c>.doc</c> file.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public static async ValueTask<WordDocument> LoadAsync(string path, CancellationToken cancellationToken = default) =>
        await LoadWithOptionsAsync(path, DocImportOptions.Default, cancellationToken).ConfigureAwait(false);

    /// <summary>Reads a legacy document from a file, with a password when it needs one.</summary>
    /// <param name="path">Path to the <c>.doc</c> file.</param>
    /// <param name="password">Password of an encrypted document.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public static async ValueTask<WordDocument> LoadAsync(
        string path, string? password, CancellationToken cancellationToken = default) =>
        await LoadWithOptionsAsync(
            path, new DocImportOptions { Password = password }, cancellationToken).ConfigureAwait(false);

    /// <summary>Reads a legacy document from a file with explicit resource limits.</summary>
    /// <param name="path">Path to the <c>.doc</c> file.</param>
    /// <param name="options">Password and resource limits.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public static async ValueTask<WordDocument> LoadWithOptionsAsync(
        string path, DocImportOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        byte[] bytes = await DocumentInput.ReadFileBytesAsync(path, options.Budget, cancellationToken).ConfigureAwait(false);
        return LoadWithOptions(bytes, options);
    }

    /// <summary>Reads a legacy document from a stream.</summary>
    /// <param name="stream">Stream positioned at the start of the file.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public static async ValueTask<WordDocument> LoadAsync(Stream stream, CancellationToken cancellationToken = default)
        => await LoadWithOptionsAsync(stream, DocImportOptions.Default, cancellationToken).ConfigureAwait(false);

    /// <summary>Reads a legacy document from a stream with explicit resource limits.</summary>
    /// <param name="stream">Stream positioned at the start of the file.</param>
    /// <param name="options">Password and resource limits.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public static async ValueTask<WordDocument> LoadWithOptionsAsync(
        Stream stream, DocImportOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(options);
        byte[] bytes = await DocumentInput.ReadBytesAsync(stream, options.Budget, cancellationToken).ConfigureAwait(false);
        return LoadWithOptions(bytes, options);
    }

    /// <summary>Reads a legacy document from bytes.</summary>
    /// <param name="bytes">The whole file.</param>
    /// <param name="password">Password of an encrypted document, when there is one.</param>
    public static WordDocument Load(byte[] bytes, string? password = null)
        => LoadWithOptions(bytes, new DocImportOptions { Password = password });

    /// <summary>Reads a legacy document from bytes with explicit resource limits.</summary>
    /// <param name="bytes">The whole file.</param>
    /// <param name="options">Password and resource limits.</param>
    public static WordDocument LoadWithOptions(byte[] bytes, DocImportOptions options)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(options);
        var loadBudget = new DocumentLoadBudgetState(options.Budget);
        DocumentLoadBudgetState.Ensure(
            nameof(DocumentLoadBudget.MaxInputBytes), options.Budget.MaxInputBytes, bytes.LongLength);
        if (!CompoundFile.IsCompoundFile(bytes))
            throw new DocFormatException("The file is not a Word 97-2003 document: the compound file signature is missing.");

        CompoundFile container = CompoundFile.Open(bytes, options.Budget);
        byte[] document = container.ReadStream("WordDocument")
            ?? throw new DocFormatException("The compound file has no WordDocument stream.");

        // Whether the file is locked is settled before anything else is looked for: a document
        // nobody has the password to is refused as locked, not as missing a table stream.
        RefuseIfUnopenable(document, options.Password);

        bool table1 = FileInformationBlock.PrefersTable1(document);
        string tableName = table1 ? "1Table" : "0Table";
        byte[] table = container.ReadStream(tableName)
            ?? container.ReadStream(table1 ? "0Table" : "1Table")
            ?? throw new DocFormatException($"The compound file has no {tableName} stream.");
        byte[] data = container.ReadStream("Data") ?? [];

        if (DocDecryptor.Protection(document).Encrypted)
            (document, table, data) = DocDecryptor.Decrypt(document, table, data, options.Password);
        FileInformationBlock fib = FileInformationBlock.Read(document);
        var context = new DocReadContext(document, table, data, fib, loadBudget)
        {
            Container = container,
        };
        WordDocument result = DocConverter.Convert(context);
        DocSummary.Apply(container.ReadStream(PropertySetStream.SummaryStream), result.Properties);
        DocSummary.ApplyDocumentSummary(container.ReadStream(PropertySetStream.DocumentSummaryStream), result);
        result.Macros = VbaProject.Read(container, MacroStorage);
        return result;
    }

    /// <summary>Refuses a locked document there is no way to open, before looking any further.</summary>
    private static void RefuseIfUnopenable(byte[] document, string? password)
    {
        if (!DocDecryptor.Protection(document).Encrypted)
            return;

        if (string.IsNullOrEmpty(password))
            throw new EncryptedDocumentException(
                "The document is encrypted. Supply the password to DocReader.Load to open it.");
    }
}

/// <summary>Everything the converter needs from the file, resolved once.</summary>
internal sealed class DocReadContext
{
    private readonly List<DocumentWarning> _warnings = [];
    private readonly HashSet<int> _claimedTextboxes = [];

    public DocReadContext(
        byte[] document,
        byte[] table,
        byte[] data,
        FileInformationBlock fib,
        DocumentLoadBudgetState loadBudget)
    {
        Document = document;
        Table = table;
        Data = data;
        Fib = fib;
        LoadBudget = loadBudget;

        (int clxOffset, int clxLength) = fib.PieceTable;
        Pieces = clxLength > 0
            ? PieceTable.Read(table, clxOffset, clxLength)
            : throw new DocFormatException("The document has no piece table, so its text cannot be located.");

        (int styleOffset, int styleLength) = fib.StyleSheet;
        Styles = styleLength > 0 ? DocStyleSheet.Read(table, styleOffset, styleLength) : DocStyleSheet.Empty;

        (int fontOffset, int fontLength) = fib.FontTable;
        Fonts = fontLength > 0 ? DocFontTable.Read(table, fontOffset, fontLength) : DocFontTable.Empty;

        (int sectionOffset, int sectionLength) = fib.SectionTable;
        Sections = DocSectionTable.Read(document, table, sectionOffset, sectionLength);

        Footnotes = DocStoryReader.Read(table, fib.FootnoteReferences, fib.FootnoteBodies, recordBytes: 2);
        Endnotes = DocStoryReader.Read(table, fib.EndnoteReferences, fib.EndnoteBodies, recordBytes: 2);
        Comments = DocStoryReader.Read(table, fib.CommentReferences, fib.CommentBodies, recordBytes: 30);
        CommentExtras = DocCommentExtra.Read(table, fib.CommentExtra, Comments.Records.Count);

        (int headerOffset, int headerLength) = fib.HeaderStories;
        HeaderStories = DocStoryReader.ReadPositions(table, headerOffset, headerLength);

        (int authorOffset, int authorLength) = fib.CommentAuthors;
        CommentAuthors = DocStringTable.ReadCountedStrings(table, authorOffset, authorLength);
        Bookmarks = DocBookmarkTable.Read(table, fib.BookmarkNames, fib.BookmarkStarts, fib.BookmarkEnds);
        CommentBookmarks = DocCommentBookmarkTable.Read(
            table, fib.CommentBookmarks, fib.CommentBookmarkStarts, fib.CommentBookmarkEnds);

        MainShapes = DocShapeTable.Read(table, fib.MainShapes);
        HeaderShapes = DocShapeTable.Read(table, fib.HeaderShapes);
        Textboxes = DocTextboxTable.Read(table, fib.Textboxes);
        HeaderTextboxes = DocTextboxTable.Read(table, fib.HeaderTextboxes);
        Drawings = OfficeArtShapes.Read(table, fib.Drawings);
        Blips = OfficeArtBlipStore.Read(table, fib.Drawings, document, loadBudget);

        Ansi = Encoding.GetEncoding(1252);
        CharacterRuns = BuildCharacterRuns();
        ParagraphRuns = BuildParagraphRuns();
    }

    /// <summary>Where the shapes anchored in the main story sit.</summary>
    public DocShapeTable MainShapes { get; }

    /// <summary>Where the shapes anchored in a header sit.</summary>
    public DocShapeTable HeaderShapes { get; }

    /// <summary>Which stretch of the text-box story belongs to which shape.</summary>
    public DocTextboxTable Textboxes { get; }

    /// <summary>The same for the text boxes anchored in a header.</summary>
    public DocTextboxTable HeaderTextboxes { get; }

    /// <summary>What each floating drawing is, keyed by the identifier its anchor names.</summary>
    public OfficeArtShapes Drawings { get; }

    /// <summary>The images the drawings display, kept once for the whole document.</summary>
    public OfficeArtBlipStore Blips { get; }

    /// <summary>
    /// The images the conversion resolved, in the order it found them. They are registered
    /// with the document once it is built, because a picture no part refers to is a package
    /// part nothing points at.
    /// </summary>
    public List<ImageData> Images { get; } = [];

    /// <summary>Resource counters shared by binary sub-readers.</summary>
    public DocumentLoadBudgetState LoadBudget { get; }

    /// <summary>What the conversion could not carry across, in the order it was found.</summary>
    public IReadOnlyList<DocumentWarning> Warnings => _warnings;

    /// <summary>The file itself, for the storages that sit beside the document's own streams.</summary>
    public CompoundFile? Container { get; init; }

    /// <summary>The embedded objects found while walking the text, in the order they appear.</summary>
    public List<EmbeddedObject> EmbeddedObjects { get; } = [];

    /// <summary>
    /// Takes a text box for the shape that draws it, once. A shape chained to another shares
    /// its story, and a file whose boxes point at each other must not read forever.
    /// </summary>
    /// <param name="shapeId">Identifier of the shape.</param>
    /// <returns><see langword="true"/> the first time a shape is asked for.</returns>
    public bool ClaimTextbox(int shapeId) => _claimedTextboxes.Add(shapeId);

    /// <summary>Whether a text box has already been read as part of the text.</summary>
    /// <param name="shapeId">Identifier of the shape.</param>
    public bool HasTextbox(int shapeId) => _claimedTextboxes.Contains(shapeId);

    public byte[] Document { get; }

    public byte[] Table { get; }

    /// <summary>The stream holding what was too large to store inline, when there is one.</summary>
    public byte[] Data { get; }

    public FileInformationBlock Fib { get; }

    public PieceTable Pieces { get; }

    public DocStyleSheet Styles { get; }

    public DocFontTable Fonts { get; }

    /// <summary>The sections of the main story, in order.</summary>
    public IReadOnlyList<DocSection> Sections { get; }

    /// <summary>Where the footnote references and bodies are.</summary>
    public DocStoryReader Footnotes { get; }

    /// <summary>Where the endnote references and bodies are.</summary>
    public DocStoryReader Endnotes { get; }

    /// <summary>Where the comment references and bodies are.</summary>
    public DocStoryReader Comments { get; }

    /// <summary>When each comment was written and what it answers, parallel to <see cref="Comments"/>.</summary>
    public IReadOnlyList<DocCommentExtra> CommentExtras { get; }

    /// <summary>Where each header, footer and note separator story begins.</summary>
    public IReadOnlyList<int> HeaderStories { get; }

    /// <summary>The names of the people who left the comments.</summary>
    public IReadOnlyList<string> CommentAuthors { get; }

    /// <summary>The bookmarks of the main story.</summary>
    public IReadOnlyList<DocBookmark> Bookmarks { get; }

    /// <summary>The stretch of text each comment applies to, keyed by the tag its record names.</summary>
    public IReadOnlyDictionary<int, (int Start, int End)> CommentBookmarks { get; }

    public Encoding Ansi { get; }

    /// <summary>
    /// Character formatting keyed by the position it starts at, read once. The formatting
    /// pages cover the whole file, so resolving them per paragraph would re-read every page
    /// for every paragraph.
    /// </summary>
    public DocCharacterRun[] CharacterRuns { get; }

    /// <summary>Paragraph formatting in order of the position each paragraph ends at.</summary>
    public (int End, FormattedRun Run)[] ParagraphRuns { get; }

    /// <summary>
    /// Records something the conversion could not carry across. The same problem is recorded
    /// once however often it occurs: a document with forty floating shapes has one thing
    /// missing from it, not forty.
    /// </summary>
    public void Warn(WarningCode code, string message)
    {
        var warning = new DocumentWarning(code, message);
        if (!_warnings.Contains(warning))
            _warnings.Add(warning);
    }

    private DocCharacterRun[] BuildCharacterRuns()
    {
        (int binOffset, int binLength) = Fib.CharacterBinTable;
        var runs = new SortedDictionary<int, DocCharacterRun>();

        foreach (int page in FormattedDiskPage.ReadBinTable(Table, binOffset, binLength))
        {
            foreach (FormattedRun run in FormattedDiskPage.ReadCharacterPage(Document, page))
            {
                int start = Pieces.PositionOf(run.Start);
                if (start < 0)
                    continue;

                runs[start] = run.Properties.Length > 0
                    ? new DocCharacterRun(
                        start,
                        SprmTranslator.ApplyRun(RunFormat.Default, run.Properties, Fonts, Styles),
                        PictureOffset(run.Properties),
                        StandsForEmbeddedObject(run.Properties))
                    : new DocCharacterRun(start, RunFormat.Default, -1, false);
            }
        }

        return [.. runs.Values];
    }

    /// <summary>
    /// Where in the data stream a run's picture lives, or <c>-1</c> when the run carries no
    /// picture. It is a character property rather than anything to do with the text.
    /// </summary>
    private static int PictureOffset(ReadOnlySpan<byte> properties)
    {
        var reader = new SprmReader(properties);
        while (reader.TryRead(out Sprm sprm))
        {
            if (sprm.Opcode == SprmCode.PictureLocation)
                return sprm.Int32;
        }

        return -1;
    }

    /// <summary>
    /// Whether the run stands in for an embedded OLE object (<c>sprmCFOle2</c>, [MS-DOC]
    /// 2.6.1). The character it marks is the separator of an <c>EMBED</c>, <c>LINK</c> or
    /// <c>CONTROL</c> field, and the object itself lives in a storage of its own.
    /// </summary>
    private static bool StandsForEmbeddedObject(ReadOnlySpan<byte> properties)
    {
        var reader = new SprmReader(properties);
        while (reader.TryRead(out Sprm sprm))
        {
            if (sprm.Opcode == SprmCode.EmbeddedObject)
                return sprm.Toggle(inherited: false) == true;
        }

        return false;
    }

    private (int End, FormattedRun Run)[] BuildParagraphRuns()
    {
        (int binOffset, int binLength) = Fib.ParagraphBinTable;
        var runs = new List<(int End, FormattedRun Run)>();

        foreach (int page in FormattedDiskPage.ReadBinTable(Table, binOffset, binLength))
        {
            foreach (FormattedRun run in FormattedDiskPage.ReadParagraphPage(Document, page))
            {
                int end = Pieces.PositionOf(run.End);
                if (end < 0)
                    end = Pieces.PositionOf(run.End - 1) + 1;
                if (end > 0)
                    runs.Add((end, run));
            }
        }

        runs.Sort(static (left, right) => left.End.CompareTo(right.End));
        return [.. runs];
    }
}

/// <summary>One stretch of uniform character formatting, and what it points at.</summary>
/// <param name="Start">Character position the stretch begins at.</param>
/// <param name="Format">Formatting in force across it.</param>
/// <param name="PictureOffset">Offset of the run's picture in the data stream, or <c>-1</c>.</param>
/// <param name="IsEmbeddedObject">Whether the run stands in for an OLE object.</param>
internal readonly record struct DocCharacterRun(int Start, RunFormat Format, int PictureOffset, bool IsEmbeddedObject);
