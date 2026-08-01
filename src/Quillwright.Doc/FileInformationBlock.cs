using System.Buffers.Binary;
using Quillwright.Diagnostics;

namespace Quillwright.Doc;

/// <summary>
/// The header of the <c>WordDocument</c> stream ([MS-DOC] 2.5.1): what version wrote the
/// file, whether it is encrypted, how the text divides into stories, and where in the table
/// stream every other structure lives.
/// </summary>
/// <remarks>
/// Everything in a legacy Word file is found through a pair of numbers in this block: a file
/// offset into the table stream and a length. The block grew over the versions, so the
/// offsets of the pairs depend on counts stored earlier in the block rather than being
/// fixed, which is why it is walked rather than mapped.
/// </remarks>
internal sealed class FileInformationBlock
{
    private readonly byte[] _stream;
    private readonly int _fibRcwLcbBase;
    private readonly int _pairCount;

    private FileInformationBlock(byte[] stream, int rcwLcbBase, int pairCount)
    {
        _stream = stream;
        _fibRcwLcbBase = rcwLcbBase;
        _pairCount = pairCount;
    }

    /// <summary>Whether the table stream is named <c>1Table</c> rather than <c>0Table</c>.</summary>
    public bool UsesTableStream1 { get; private init; }

    /// <summary>Characters in the main document story.</summary>
    public int MainTextLength { get; private init; }

    /// <summary>Characters in the footnote story.</summary>
    public int FootnoteTextLength { get; private init; }

    /// <summary>Characters in the header and footer story.</summary>
    public int HeaderTextLength { get; private init; }

    /// <summary>Characters in the comment story.</summary>
    public int CommentTextLength { get; private init; }

    /// <summary>Characters in the endnote story.</summary>
    public int EndnoteTextLength { get; private init; }

    /// <summary>Characters in the text-box story.</summary>
    public int TextboxTextLength { get; private init; }

    /// <summary>Characters in the story of the text boxes anchored in a header.</summary>
    public int HeaderTextboxTextLength { get; private init; }

    /// <summary>
    /// Which stream the file keeps its table in, read from the header before anything else.
    /// </summary>
    /// <param name="stream">The <c>WordDocument</c> stream.</param>
    /// <remarks>
    /// A locked file has to be decrypted before its header can be read properly, and the
    /// table stream is one of the three that need decrypting — so which one it is has to come
    /// out of the handful of bytes that stay readable whatever else is scrambled.
    /// </remarks>
    public static bool PrefersTable1(byte[] stream) =>
        stream.Length >= 12 && (BinaryPrimitives.ReadUInt16LittleEndian(stream.AsSpan(10)) & 0x0200) != 0;

    /// <summary>Reads and validates the block at the start of the document stream.</summary>
    /// <param name="stream">The <c>WordDocument</c> stream.</param>
    public static FileInformationBlock Read(byte[] stream)
    {
        if (stream.Length < 512)
            throw new DocFormatException("The document stream is too short to hold a file information block.");

        ushort identifier = BinaryPrimitives.ReadUInt16LittleEndian(stream);
        if (identifier != 0xA5EC)
            throw new DocFormatException($"Unexpected document signature 0x{identifier:X4}; this is not a Word 97-2003 document.");

        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(stream.AsSpan(2));
        if (version < 101)
            throw new DocFormatException($"Word version {version} predates the Word 97 format, which is the oldest this reader understands.");

        // Whether the file is encrypted is settled before this point: the reader decrypts the
        // streams first, and the flag stays set in the header it hands back.
        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(stream.AsSpan(10));

        // The variable-length arrays that precede the offset pairs each declare their own
        // length, so the base of the pairs is found by stepping over them.
        int position = 32;
        position += 2 + (BinaryPrimitives.ReadUInt16LittleEndian(stream.AsSpan(position)) * 2);
        int fibRgLw = position;
        position += 2 + (BinaryPrimitives.ReadUInt16LittleEndian(stream.AsSpan(position)) * 4);

        return new FileInformationBlock(stream, position + 2, BinaryPrimitives.ReadUInt16LittleEndian(stream.AsSpan(position)))
        {
            UsesTableStream1 = (flags & 0x0200) != 0,
            MainTextLength = BinaryPrimitives.ReadInt32LittleEndian(stream.AsSpan(fibRgLw + 2 + 12)),
            FootnoteTextLength = BinaryPrimitives.ReadInt32LittleEndian(stream.AsSpan(fibRgLw + 2 + 16)),
            HeaderTextLength = BinaryPrimitives.ReadInt32LittleEndian(stream.AsSpan(fibRgLw + 2 + 20)),
            CommentTextLength = BinaryPrimitives.ReadInt32LittleEndian(stream.AsSpan(fibRgLw + 2 + 28)),
            EndnoteTextLength = BinaryPrimitives.ReadInt32LittleEndian(stream.AsSpan(fibRgLw + 2 + 32)),
            TextboxTextLength = BinaryPrimitives.ReadInt32LittleEndian(stream.AsSpan(fibRgLw + 2 + 36)),
            HeaderTextboxTextLength = BinaryPrimitives.ReadInt32LittleEndian(stream.AsSpan(fibRgLw + 2 + 40)),
        };
    }

    /// <summary>The offset and length pair at a given index of the table-stream directory.</summary>
    /// <param name="index">Index of the pair, as numbered in [MS-DOC] 2.5.5.</param>
    /// <remarks>
    /// The directory grew with each version, and the block says how many pairs it really has.
    /// Asking for one a Word 97 file never had would otherwise read the text that follows the
    /// block and take it for an offset, so an index past the end answers with nothing.
    /// </remarks>
    public (int Offset, int Length) Pair(int index)
    {
        if (index >= _pairCount)
            return (0, 0);

        int position = _fibRcwLcbBase + (index * 8);
        if (position + 8 > _stream.Length)
            return (0, 0);
        return (
            BinaryPrimitives.ReadInt32LittleEndian(_stream.AsSpan(position)),
            BinaryPrimitives.ReadInt32LittleEndian(_stream.AsSpan(position + 4)));
    }

    /// <summary>The complex-file piece table (<c>fcClx</c>).</summary>
    public (int Offset, int Length) PieceTable => Pair(33);

    /// <summary>The style sheet (<c>fcStshf</c>).</summary>
    public (int Offset, int Length) StyleSheet => Pair(1);

    /// <summary>The font name table (<c>fcSttbfFfn</c>).</summary>
    public (int Offset, int Length) FontTable => Pair(15);

    /// <summary>The bin table of character formatting (<c>fcPlcfBteChpx</c>).</summary>
    public (int Offset, int Length) CharacterBinTable => Pair(12);

    /// <summary>The bin table of paragraph formatting (<c>fcPlcfBtePapx</c>).</summary>
    public (int Offset, int Length) ParagraphBinTable => Pair(13);

    /// <summary>The section descriptors (<c>fcPlcfSed</c>).</summary>
    public (int Offset, int Length) SectionTable => Pair(6);

    /// <summary>The document properties (<c>fcDop</c>).</summary>
    public (int Offset, int Length) Properties => Pair(31);

    /// <summary>Footnote reference positions (<c>fcPlcffndRef</c>).</summary>
    public (int Offset, int Length) FootnoteReferences => Pair(2);

    /// <summary>Footnote body positions (<c>fcPlcffndTxt</c>).</summary>
    public (int Offset, int Length) FootnoteBodies => Pair(3);

    /// <summary>Comment reference positions (<c>fcPlcfandRef</c>).</summary>
    public (int Offset, int Length) CommentReferences => Pair(4);

    /// <summary>Comment body positions (<c>fcPlcfandTxt</c>).</summary>
    public (int Offset, int Length) CommentBodies => Pair(5);

    /// <summary>Header and footer story boundaries (<c>fcPlcfHdd</c>).</summary>
    public (int Offset, int Length) HeaderStories => Pair(11);

    /// <summary>Comment author names (<c>fcGrpXstAtnOwners</c>).</summary>
    public (int Offset, int Length) CommentAuthors => Pair(36);

    /// <summary>The bookmarks that record what each comment applies to (<c>fcSttbfAtnBkmk</c>).</summary>
    public (int Offset, int Length) CommentBookmarks => Pair(37);

    /// <summary>Where each comment's bookmark opens (<c>fcPlcfAtnBkf</c>).</summary>
    public (int Offset, int Length) CommentBookmarkStarts => Pair(42);

    /// <summary>Where each comment's bookmark closes (<c>fcPlcfAtnBkl</c>).</summary>
    public (int Offset, int Length) CommentBookmarkEnds => Pair(43);

    /// <summary>
    /// The dates and threading of the comments (<c>fcAtrdExtra</c>). Word 2002 appended it
    /// after the ninety-third pair of Word 97 and the fifteen of Word 2000, so a file older
    /// than that has a shorter directory and <see cref="Pair"/> reads past the end of it —
    /// which it answers with an empty region rather than a wrong one.
    /// </summary>
    public (int Offset, int Length) CommentExtra => Pair(112);

    /// <summary>List definitions (<c>fcPlfLst</c>).</summary>
    public (int Offset, int Length) ListDefinitions => Pair(73);

    /// <summary>List overrides (<c>fcPlfLfo</c>).</summary>
    public (int Offset, int Length) ListOverrides => Pair(74);

    /// <summary>Bookmark names (<c>fcSttbfBkmk</c>).</summary>
    public (int Offset, int Length) BookmarkNames => Pair(21);

    /// <summary>Where each bookmark opens (<c>fcPlcfBkf</c>).</summary>
    public (int Offset, int Length) BookmarkStarts => Pair(22);

    /// <summary>Where each bookmark closes (<c>fcPlcfBkl</c>).</summary>
    public (int Offset, int Length) BookmarkEnds => Pair(23);

    /// <summary>
    /// The toolbars and key bindings the document customises (<c>fcCmds</c>, a <c>Tcg</c> of
    /// [MS-DOC] 2.9.351, whose identifiers are tabulated in [MS-CTDOC]).
    /// </summary>
    public (int Offset, int Length) CommandTable => Pair(24);

    /// <summary>Endnote reference positions (<c>fcPlcfendRef</c>).</summary>
    public (int Offset, int Length) EndnoteReferences => Pair(46);

    /// <summary>Endnote body positions (<c>fcPlcfendTxt</c>).</summary>
    public (int Offset, int Length) EndnoteBodies => Pair(47);

    /// <summary>Where the shapes of the main story are anchored (<c>fcPlcSpaMom</c>).</summary>
    public (int Offset, int Length) MainShapes => Pair(40);

    /// <summary>Where the shapes of the header story are anchored (<c>fcPlcSpaHdr</c>).</summary>
    public (int Offset, int Length) HeaderShapes => Pair(41);

    /// <summary>The document's drawings (<c>fcDggInfo</c>), an <c>OfficeArtContent</c>.</summary>
    public (int Offset, int Length) Drawings => Pair(50);

    /// <summary>Which stretch of the text-box story belongs to which shape (<c>fcPlcftxbxTxt</c>).</summary>
    public (int Offset, int Length) Textboxes => Pair(56);

    /// <summary>The same for text boxes anchored in a header (<c>fcPlcfHdrtxbxTxt</c>).</summary>
    public (int Offset, int Length) HeaderTextboxes => Pair(58);
}
