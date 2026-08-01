using System.Buffers.Binary;

namespace Quillwright.Doc.Writing;

/// <summary>
/// Packs property lists into the 512-byte pages the document stream stores them in
/// ([MS-DOC] 2.9.23 <c>ChpxFkp</c> and 2.9.174 <c>PapxFkp</c>).
/// </summary>
/// <remarks>
/// <para>
/// A page is a fixed-size block that grows from both ends: file offsets and their index
/// entries accumulate from the front, the property lists themselves from the back, and the
/// very last byte counts how many entries the page holds. When the two halves meet, the page
/// is closed and a new one started, which is why packing has to be done greedily rather than
/// by dividing the work up in advance.
/// </para>
/// <para>
/// Paragraph pages carry more per entry than character pages — thirteen bytes against one —
/// and cap out sooner, and a single paragraph's properties can be too large for a page at
/// all. Those are handed to the data stream and referred to indirectly.
/// </para>
/// </remarks>
internal sealed class FkpBuilder
{
    private const int PageSize = 512;
    private const int MaximumCharacterRuns = 0x65;
    private const int MaximumParagraphs = 0x1D;
    private const int ParagraphEntryBytes = 13;

    private readonly List<byte[]> _pages = [];

    /// <summary>The pages, in the order they must be written.</summary>
    public IReadOnlyList<byte[]> Pages => _pages;

    /// <summary>Packs the character-property runs of a story.</summary>
    /// <param name="runs">The runs, in position order.</param>
    /// <param name="offset">Maps a character position to its file offset.</param>
    public static FkpBuilder ForCharacters(IReadOnlyList<RunSpanRecord> runs, Func<int, int> offset)
    {
        var builder = new FkpBuilder();
        var page = new Page(MaximumCharacterRuns, entryBytes: 1);

        foreach (RunSpanRecord run in runs)
        {
            byte[] properties = CharacterEntry(run.Properties);
            if (!page.TryAdd(offset(run.StartPosition), offset(run.EndPosition), properties))
            {
                builder.Close(page);
                page = new Page(MaximumCharacterRuns, entryBytes: 1);
                page.TryAdd(offset(run.StartPosition), offset(run.EndPosition), properties);
            }
        }

        builder.Close(page);
        return builder;
    }

    /// <summary>Packs the paragraph-property lists of a story.</summary>
    /// <param name="paragraphs">The paragraphs, in position order.</param>
    /// <param name="offset">Maps a character position to its file offset.</param>
    /// <param name="start">The file offset of the first paragraph.</param>
    /// <param name="overflow">Stores property lists too large for a page.</param>
    public static FkpBuilder ForParagraphs(
        IReadOnlyList<ParagraphSpan> paragraphs,
        Func<int, int> offset,
        int start,
        Func<byte[], int> overflow)
    {
        var builder = new FkpBuilder();
        var page = new Page(MaximumParagraphs, ParagraphEntryBytes);
        int previous = start;

        foreach (ParagraphSpan paragraph in paragraphs)
        {
            byte[] properties = ParagraphEntry(paragraph, overflow);
            int end = offset(paragraph.EndPosition);
            if (!page.TryAdd(previous, end, properties))
            {
                builder.Close(page);
                page = new Page(MaximumParagraphs, ParagraphEntryBytes);
                page.TryAdd(previous, end, properties);
            }

            previous = end;
        }

        builder.Close(page);
        return builder;
    }

    /// <summary>Wraps character properties in the byte count that precedes them.</summary>
    private static byte[] CharacterEntry(byte[] properties)
    {
        if (properties.Length > byte.MaxValue)
            properties = properties[..byte.MaxValue];

        var entry = new byte[1 + properties.Length];
        entry[0] = (byte)properties.Length;
        properties.CopyTo(entry, 1);
        return entry;
    }

    /// <summary>
    /// Wraps paragraph properties in their style index and length. A list that cannot fit in
    /// a page is replaced by one modifier pointing at a copy in the data stream.
    /// </summary>
    private static byte[] ParagraphEntry(ParagraphSpan paragraph, Func<byte[], int> overflow)
    {
        byte[] entry = Pack(paragraph.StyleIndex, paragraph.Properties);
        if (entry.Length <= PageSize - 9 - ParagraphEntryBytes)
            return entry;

        var indirect = new GrpprlWriter();
        indirect.Int32(SprmCode.HugeParagraphProperties, overflow(paragraph.Properties));
        return Pack(paragraph.StyleIndex, indirect.ToArray());
    }

    /// <summary>
    /// Lays out one paragraph's properties: a size in words, the style index, then the
    /// modifiers. When the size does not fit in a byte, a leading zero says so and the real
    /// size follows.
    /// </summary>
    private static byte[] Pack(int styleIndex, byte[] properties)
    {
        var body = new byte[2 + properties.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(body, (ushort)styleIndex);
        properties.CopyTo(body, 2);

        // The stored count is of word pairs and covers one byte less than it measures, so the
        // body is padded to an odd length for the arithmetic to come out whole.
        int padded = body.Length % 2 == 0 ? body.Length + 1 : body.Length;
        var entry = new byte[1 + padded];
        entry[0] = (byte)((padded + 1) / 2);
        body.CopyTo(entry, 1);
        return entry;
    }

    private void Close(Page page)
    {
        if (page.Count > 0)
            _pages.Add(page.ToArray());
    }

    /// <summary>One page, filled from both ends until the halves meet.</summary>
    private sealed class Page(int maximumEntries, int entryBytes)
    {
        private readonly List<int> _offsets = [];
        private readonly List<byte[]> _properties = [];

        public int Count => _properties.Count;

        public bool TryAdd(int start, int end, byte[] properties)
        {
            if (_offsets.Count == 0)
                _offsets.Add(start);
            else if (_offsets[^1] != start)
                return false;

            if (_properties.Count >= maximumEntries)
                return false;

            // Property lists sit on even boundaries because the index refers to them in
            // words, so the worst case has to allow for a padding byte apiece.
            int entries = _properties.Count + 1;
            int front = 4 + (entries * (4 + entryBytes)) + 1;
            int back = _properties.Sum(static p => p.Length + (p.Length % 2)) + properties.Length + (properties.Length % 2);
            if (front + back + 1 > PageSize)
                return false;

            _offsets.Add(end);
            _properties.Add(properties);
            return true;
        }

        public byte[] ToArray()
        {
            var page = new byte[PageSize];
            for (int i = 0; i < _offsets.Count; i++)
                BinaryPrimitives.WriteInt32LittleEndian(page.AsSpan(i * 4), _offsets[i]);

            int indexBase = _offsets.Count * 4;
            int cursor = PageSize - 1;
            page[cursor] = (byte)_properties.Count;

            for (int i = 0; i < _properties.Count; i++)
            {
                byte[] properties = _properties[i];
                cursor = (cursor - properties.Length) & ~1;
                properties.CopyTo(page.AsSpan(cursor));

                int slot = indexBase + (i * entryBytes);
                page[slot] = (byte)(cursor / 2);
            }

            return page;
        }
    }
}
