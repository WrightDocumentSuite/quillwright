using System.Buffers.Binary;

namespace Quillwright.Doc.Writing;

/// <summary>
/// Builds the file information block ([MS-DOC] 2.5.1) that opens the document stream and
/// points at everything else.
/// </summary>
/// <remarks>
/// <para>
/// The block grew with each version by appending to its tail, and a reader works out which
/// shape it is looking at from counts stored inside it. The specification allows the
/// original Word 97 shape — ninety-three offset pairs and no extension — but Word itself
/// refuses to open a file written that way, so the block is written in the shape Word emits:
/// the same version number followed by an extension that supersedes it with 0x0112, and the
/// longer directory of a hundred and eighty-three pairs that goes with it.
/// </para>
/// <para>
/// That makes the block larger than a sector, so the text begins two sectors in rather than
/// one.
/// </para>
/// </remarks>
internal sealed class FibBuilder
{
    /// <summary>Offset in the document stream where the text is written.</summary>
    public const int TextStart = 2048;

    private const int PairCount = 0x00B7;
    private const int BaseBytes = 32;
    private const int ShortCount = 0x000E;
    private const int LongCount = 0x0016;
    private const int ExtensionCount = 0x0005;

    private readonly int[] _offsets = new int[PairCount];
    private readonly int[] _lengths = new int[PairCount];

    /// <summary>Total size of the block, which the text is written after.</summary>
    public static int Size =>
        BaseBytes + 2 + (ShortCount * 2) + 2 + (LongCount * 4) + 2 + (PairCount * 8) + 2 + (ExtensionCount * 2);

    /// <summary>Bytes of the document stream that mean anything.</summary>
    public int SignificantBytes { get; set; }

    /// <summary>Characters in the main story.</summary>
    public int MainLength { get; set; }

    /// <summary>Characters in the footnote story.</summary>
    public int FootnoteLength { get; set; }

    /// <summary>Characters in the header story.</summary>
    public int HeaderLength { get; set; }

    /// <summary>Characters in the comment story.</summary>
    public int CommentLength { get; set; }

    /// <summary>Characters in the endnote story.</summary>
    public int EndnoteLength { get; set; }

    /// <summary>Whether the document contains at least one picture.</summary>
    public bool HasPictures { get; set; }

    /// <summary>
    /// Bytes of the table stream the encryption header occupies, or zero when the file is not
    /// locked. Setting it also sets the flag that says the content is encrypted
    /// ([MS-DOC] 2.5.15, <c>fEncrypted</c> and <c>lKey</c>).
    /// </summary>
    public int EncryptionHeaderBytes { get; set; }

    /// <summary>Records where a structure lives in the table stream.</summary>
    /// <param name="index">Index of the pair, as numbered in [MS-DOC] 2.5.5.</param>
    /// <param name="offset">Offset in the table stream.</param>
    /// <param name="length">Length in bytes; a zero-length structure is not recorded.</param>
    public void Set(int index, int offset, int length)
    {
        if (length <= 0)
            return;

        _offsets[index] = offset;
        _lengths[index] = length;
    }

    /// <summary>Writes the block.</summary>
    public byte[] ToArray()
    {
        var bytes = new byte[Size];
        Span<byte> span = bytes;

        BinaryPrimitives.WriteUInt16LittleEndian(span, 0xA5EC);
        BinaryPrimitives.WriteUInt16LittleEndian(span[2..], 0x00C1);
        BinaryPrimitives.WriteUInt16LittleEndian(span[6..], 0x0409);

        // cQuickSaves is required to be all ones once the version is superseded; fWhichTblStm
        // selects the stream named 1Table, and fExtChar is required to be set.
        ushort flags = 0x00F0 | 0x0200 | 0x1000;
        if (HasPictures)
            flags |= 0x0008;
        if (EncryptionHeaderBytes > 0)
            flags |= 0x0100;
        BinaryPrimitives.WriteUInt16LittleEndian(span[10..], flags);
        BinaryPrimitives.WriteUInt16LittleEndian(span[12..], 0x00BF);
        BinaryPrimitives.WriteInt32LittleEndian(span[14..], EncryptionHeaderBytes);

        int cursor = BaseBytes;
        BinaryPrimitives.WriteUInt16LittleEndian(span[cursor..], ShortCount);
        cursor += 2 + (ShortCount * 2);

        BinaryPrimitives.WriteUInt16LittleEndian(span[cursor..], LongCount);
        Span<byte> counts = span[(cursor + 2)..];
        BinaryPrimitives.WriteInt32LittleEndian(counts, SignificantBytes);
        BinaryPrimitives.WriteInt32LittleEndian(counts[12..], MainLength);
        BinaryPrimitives.WriteInt32LittleEndian(counts[16..], FootnoteLength);
        BinaryPrimitives.WriteInt32LittleEndian(counts[20..], HeaderLength);
        BinaryPrimitives.WriteInt32LittleEndian(counts[28..], CommentLength);
        BinaryPrimitives.WriteInt32LittleEndian(counts[32..], EndnoteLength);
        cursor += 2 + (LongCount * 4);

        BinaryPrimitives.WriteUInt16LittleEndian(span[cursor..], PairCount);
        Span<byte> pairs = span[(cursor + 2)..];
        for (int i = 0; i < PairCount; i++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(pairs[(i * 8)..], _offsets[i]);
            BinaryPrimitives.WriteInt32LittleEndian(pairs[((i * 8) + 4)..], _lengths[i]);
        }

        cursor += 2 + (PairCount * 8);
        BinaryPrimitives.WriteUInt16LittleEndian(span[cursor..], ExtensionCount);
        BinaryPrimitives.WriteUInt16LittleEndian(span[(cursor + 2)..], 0x0112);
        return bytes;
    }

    /// <summary>Index of each structure in the table-stream directory ([MS-DOC] 2.5.5).</summary>
    public static class Pair
    {
        /// <summary>The stylesheet.</summary>
        public const int StyleSheet = 1;

        /// <summary>Footnote references.</summary>
        public const int FootnoteReferences = 2;

        /// <summary>Footnote bodies.</summary>
        public const int FootnoteText = 3;

        /// <summary>Comment references.</summary>
        public const int CommentReferences = 4;

        /// <summary>Comment bodies.</summary>
        public const int CommentText = 5;

        /// <summary>Section descriptors.</summary>
        public const int Sections = 6;

        /// <summary>Header and footer story boundaries.</summary>
        public const int Headers = 11;

        /// <summary>Where each page of character properties lives.</summary>
        public const int CharacterBinTable = 12;

        /// <summary>Where each page of paragraph properties lives.</summary>
        public const int ParagraphBinTable = 13;

        /// <summary>The font names.</summary>
        public const int Fonts = 15;

        /// <summary>Field boundaries in the main story.</summary>
        public const int MainFields = 16;

        /// <summary>Field boundaries in the header story.</summary>
        public const int HeaderFields = 17;

        /// <summary>Field boundaries in the footnote story.</summary>
        public const int FootnoteFields = 18;

        /// <summary>Field boundaries in the comment story.</summary>
        public const int CommentFields = 19;

        /// <summary>Bookmark names.</summary>
        public const int BookmarkNames = 21;

        /// <summary>Where each bookmark opens.</summary>
        public const int BookmarkStarts = 22;

        /// <summary>Where each bookmark closes.</summary>
        public const int BookmarkEnds = 23;

        /// <summary>Comment authors.</summary>
        public const int CommentAuthors = 36;

        /// <summary>The bookmarks that record what each comment applies to.</summary>
        public const int CommentBookmarks = 37;

        /// <summary>Where each comment's bookmark opens.</summary>
        public const int CommentBookmarkStarts = 42;

        /// <summary>Where each comment's bookmark closes.</summary>
        public const int CommentBookmarkEnds = 43;

        /// <summary>
        /// The dates and threading of the comments, appended by Word 2002 after the
        /// ninety-three pairs of Word 97 and the fifteen of Word 2000.
        /// </summary>
        public const int CommentExtra = 112;

        /// <summary>List definitions.</summary>
        public const int ListDefinitions = 73;

        /// <summary>List overrides.</summary>
        public const int ListOverrides = 74;

        /// <summary>The document properties.</summary>
        public const int Properties = 31;

        /// <summary>The piece table.</summary>
        public const int PieceTable = 33;

        /// <summary>Endnote references.</summary>
        public const int EndnoteReferences = 46;

        /// <summary>Endnote bodies.</summary>
        public const int EndnoteText = 47;
    }
}
