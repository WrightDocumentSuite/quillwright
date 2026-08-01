using System.Buffers.Binary;
using System.Text;
using Quillwright.Diagnostics;
using Quillwright.IO;
using Quillwright.Model;

namespace Quillwright.Doc.Writing;

/// <summary>
/// Assembles the three streams of a legacy Word file and hands them to the container writer.
/// </summary>
/// <remarks>
/// <para>
/// The document stream holds the header, then the text of every story, then the pages of
/// formatting. The table stream holds everything that is indexed by position rather than
/// stored inline. The data stream holds what is too large for either, and only exists when
/// something points into it.
/// </para>
/// <para>
/// Order matters twice over: the text has to be laid down before the formatting can name
/// byte offsets in it, and the header has to be written last because it records where all of
/// that ended up.
/// </para>
/// </remarks>
internal sealed class DocSaver
{
    private readonly WordDocument _document;
    private readonly DocWriteContext _context;
    private readonly DocWriteOptions _options;
    private readonly List<byte> _table = [];
    private readonly FibBuilder _fib = new();

    public DocSaver(WordDocument document, DocWriteOptions options)
    {
        _document = document;
        _options = options;
        _context = new DocWriteContext(document, options);

        // A locked file keeps its encryption header at the front of the table stream, and
        // every offset the header block records is measured from there, so the space is
        // reserved before anything is written into it rather than prepended afterwards.
        if (options.Password is { Length: > 0 })
            _table.AddRange(new byte[DocEncryptor.HeaderLength]);
    }

    /// <summary>Builds the whole file.</summary>
    public byte[] Build()
    {
        if (_document.Macros is { Modules.Count: > 0 })
        {
            _context.Warn(
                WarningCode.PreservedVerbatim,
                "The VBA project is not written to .doc and the saved file will hold no macros.");
        }

        WarnAboutCommentThreads();

        var story = new StoryAssembler(_context);
        story.WriteMainStory(_document);
        SubStoryWriter.Write(_context, story, _document);

        byte[] text = Encoding.Unicode.GetBytes(story.Text);
        var stream = new List<byte>(FibBuilder.TextStart + text.Length + 4096);
        stream.AddRange(new byte[FibBuilder.TextStart]);
        stream.AddRange(text);

        WriteSections(stream, story);
        WriteFormatting(stream, story);
        WriteTableStream(story);

        _fib.SignificantBytes = stream.Count;
        _fib.MainLength = story.MainLength;
        _fib.FootnoteLength = story.FootnoteLength;
        _fib.HeaderLength = story.HeaderLength;
        _fib.CommentLength = story.CommentLength;
        _fib.EndnoteLength = story.EndnoteLength;
        _fib.HasPictures = !_context.Pictures.IsEmpty;

        byte[] document = [.. stream];
        byte[] table = [.. _table];
        byte[] pictures = _context.Pictures.IsEmpty ? [] : _context.Pictures.ToArray();

        // The header has to say the file is locked before it is locked, because it is one of
        // the bytes the lock covers; the streams are then encrypted with it already in place.
        if (_options.Password is { Length: > 0 } password)
            _fib.EncryptionHeaderBytes = DocEncryptor.HeaderLength;

        _fib.ToArray().CopyTo(document.AsSpan());
        if (_options.Password is { Length: > 0 } key)
            (document, table, pictures) = DocEncryptor.Encrypt(document, table, pictures, key);

        var container = new CompoundFileWriter();
        container.Add("WordDocument", document);
        container.Add("1Table", table);
        if (pictures.Length > 0)
            container.Add("Data", pictures);
        if (DocSummary.Build(_document.Properties) is { Length: > 0 } summary)
            container.Add(PropertySetStream.SummaryStream, summary);

        byte[] documentSummary = DocSummary.BuildDocumentSummary(
            _document.Properties, _document.ApplicationProperties, _document.CustomProperties);
        if (documentSummary.Length > 0)
            container.Add(PropertySetStream.DocumentSummaryStream, documentSummary);

        return container.Build();
    }

    /// <summary>
    /// Says so when something about a conversation between reviewers is about to be lost.
    /// </summary>
    /// <remarks>
    /// Who answers whom survives, because the comment tree of <c>AtrdExtra</c> ([MS-DOC]
    /// 2.9.5) says it, and so does each comment's date. What has no field at all is the flag
    /// that marks a thread settled, and the reactions and author identities the newer
    /// <c>.docx</c> parts carry.
    /// </remarks>
    private void WarnAboutCommentThreads()
    {
        int resolved = _document.Comments.Count(static comment => comment.IsResolved);
        int reactions = _document.Comments.Count(static comment => comment.ExtensibleExtLstXml is not null);
        if (resolved == 0 && reactions == 0)
            return;

        var lost = new List<string>(2);
        if (resolved > 0)
            lost.Add($"{resolved} of {_document.Comments.Count} comments are marked resolved, which is dropped");
        if (reactions > 0)
            lost.Add($"{reactions} carry reactions, which are dropped");

        _context.Warn(
            WarningCode.PreservedVerbatim,
            $"The binary format holds a comment's date, author and what it answers, but not the rest: {string.Join("; ", lost)}.");
    }

    /// <summary>
    /// Places the section properties in the document stream and the descriptors that point
    /// at them in the table stream.
    /// </summary>
    private void WriteSections(List<byte> stream, StoryAssembler story)
    {
        var descriptors = new PlcBuilder(12);
        Span<byte> descriptor = stackalloc byte[12];

        for (int i = 0; i < story.Sections.Count; i++)
        {
            byte[] properties = story.Sections[i].Properties;
            int at = stream.Count;
            stream.Add((byte)properties.Length);
            stream.Add((byte)(properties.Length >> 8));
            stream.AddRange(properties);

            descriptor.Clear();
            BinaryPrimitives.WriteInt32LittleEndian(descriptor[2..], at);
            BinaryPrimitives.WriteInt32LittleEndian(descriptor[8..], -1);

            int end = i + 1 < story.Sections.Count ? story.Sections[i + 1].StartPosition : story.MainLength;
            descriptors.Add(story.Sections[i].StartPosition, end, descriptor);
        }

        AddToTable(FibBuilder.Pair.Sections, descriptors.ToArray());
    }

    /// <summary>Writes the pages of formatting and the tables that say which page covers what.</summary>
    private void WriteFormatting(List<byte> stream, StoryAssembler story)
    {
        int Offset(int position) => FibBuilder.TextStart + (position * 2);

        FkpBuilder paragraphs = FkpBuilder.ForParagraphs(
            story.Paragraphs,
            Offset,
            FibBuilder.TextStart,
            _context.Pictures.StoreProperties);
        FkpBuilder characters = FkpBuilder.ForCharacters(story.Runs, Offset);

        // Formatting pages are located by page number rather than by offset, so each block
        // has to begin on a page boundary.
        AddToTable(
            FibBuilder.Pair.ParagraphBinTable,
            PlcBuilder.BinTable(Boundaries(story.Paragraphs, Offset, paragraphs), Page(Align(stream))));
        Append(stream, paragraphs);

        AddToTable(
            FibBuilder.Pair.CharacterBinTable,
            PlcBuilder.BinTable(RunBoundaries(story.Runs, Offset, characters), Page(Align(stream))));
        Append(stream, characters);
    }

    private static int Align(List<byte> stream)
    {
        while (stream.Count % 512 != 0)
            stream.Add(0);
        return stream.Count;
    }

    /// <summary>Writes everything the table stream holds, in the order it is laid out.</summary>
    private void WriteTableStream(StoryAssembler story)
    {
        AddToTable(FibBuilder.Pair.PieceTable, PieceTable(story));
        SubStoryWriter.WriteTables(_context, story, _fib, AddToTable);
        FieldTable.Write(_context, story, _fib, AddToTable);

        if (!_context.Lists.IsEmpty)
        {
            AddToTable(FibBuilder.Pair.ListDefinitions, _context.Lists.BuildDefinitions());
            AddToTable(FibBuilder.Pair.ListOverrides, _context.Lists.BuildOverrides());
        }

        AddToTable(FibBuilder.Pair.StyleSheet, _context.Styles.ToArray(_context.Fonts));
        AddToTable(FibBuilder.Pair.Fonts, _context.Fonts.ToArray());
        AddToTable(FibBuilder.Pair.Properties, DopBuilder.Build(_document));
    }

    /// <summary>
    /// Writes the map from character positions to bytes. Everything is written as UTF-16 in
    /// one contiguous block, so the map has a single entry.
    /// </summary>
    private static byte[] PieceTable(StoryAssembler story)
    {
        var bytes = new byte[21];
        bytes[0] = 2;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(1), 16);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(5), 0);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(9), story.Text.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(15), FibBuilder.TextStart);
        return bytes;
    }

    private static int Page(int offset) => offset / 512;

    private static void Append(List<byte> stream, FkpBuilder pages)
    {
        foreach (byte[] page in pages.Pages)
            stream.AddRange(page);
    }

    /// <summary>The file offsets at which each page of paragraph formatting takes over.</summary>
    private static List<int> Boundaries(IReadOnlyList<ParagraphSpan> paragraphs, Func<int, int> offset, FkpBuilder pages)
    {
        var boundaries = new List<int> { FibBuilder.TextStart };
        int index = 0;
        foreach (byte[] page in pages.Pages)
        {
            index += page[511];
            boundaries.Add(offset(paragraphs[Math.Min(index, paragraphs.Count) - 1].EndPosition));
        }

        return boundaries;
    }

    /// <summary>The file offsets at which each page of character formatting takes over.</summary>
    private static List<int> RunBoundaries(IReadOnlyList<RunSpanRecord> runs, Func<int, int> offset, FkpBuilder pages)
    {
        var boundaries = new List<int> { FibBuilder.TextStart };
        int index = 0;
        foreach (byte[] page in pages.Pages)
        {
            index += page[511];
            boundaries.Add(offset(runs[Math.Min(index, runs.Count) - 1].EndPosition));
        }

        return boundaries;
    }

    private void AddToTable(int pair, byte[] content)
    {
        if (content.Length == 0)
            return;

        _fib.Set(pair, _table.Count, content.Length);
        _table.AddRange(content);
        while (_table.Count % 4 != 0)
            _table.Add(0);
    }
}
