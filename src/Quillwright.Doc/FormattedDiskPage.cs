using System.Buffers.Binary;

namespace Quillwright.Doc;

/// <summary>A stretch of the document stream and the property list that applies to it.</summary>
/// <param name="Start">Byte offset of the first character.</param>
/// <param name="End">Byte offset one past the last character.</param>
/// <param name="Properties">The <c>grpprl</c>, a packed list of property modifiers.</param>
/// <param name="StyleIndex">Style index for a paragraph run, or <c>-1</c> for a character run.</param>
internal readonly record struct FormattedRun(int Start, int End, byte[] Properties, int StyleIndex);

/// <summary>
/// Reads the 512-byte pages that hold formatting ([MS-DOC] 2.9.66).
/// </summary>
/// <remarks>
/// Word kept formatting out of the text stream: the text is one long run of characters, and
/// the properties that apply to parts of it live in separate pages indexed by byte offset.
/// A page lists the offsets it covers, then the property lists themselves packed from the
/// other end of the page, which is what makes the layout look inside out.
/// </remarks>
internal static class FormattedDiskPage
{
    private const int PageSize = 512;

    /// <summary>The page numbers a bin table points at ([MS-DOC] 2.8.6).</summary>
    /// <param name="table">The table stream.</param>
    /// <param name="offset">Offset of the bin table.</param>
    /// <param name="length">Its length in bytes.</param>
    public static List<int> ReadBinTable(byte[] table, int offset, int length)
    {
        var pages = new List<int>();
        if (length < 8 || offset + length > table.Length)
            return pages;

        int count = (length - 4) / 8;
        for (int i = 0; i < count; i++)
            pages.Add(BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(offset + ((count + 1) * 4) + (i * 4))));

        return pages;
    }

    /// <summary>Reads the paragraph runs of a page.</summary>
    /// <param name="document">The <c>WordDocument</c> stream.</param>
    /// <param name="pageNumber">Page number from the bin table.</param>
    public static IEnumerable<FormattedRun> ReadParagraphPage(byte[] document, int pageNumber) =>
        Read(document, pageNumber, entrySize: 13, isParagraph: true);

    /// <summary>Reads the character runs of a page.</summary>
    /// <param name="document">The <c>WordDocument</c> stream.</param>
    /// <param name="pageNumber">Page number from the bin table.</param>
    public static IEnumerable<FormattedRun> ReadCharacterPage(byte[] document, int pageNumber) =>
        Read(document, pageNumber, entrySize: 1, isParagraph: false);

    private static IEnumerable<FormattedRun> Read(byte[] document, int pageNumber, int entrySize, bool isParagraph)
    {
        int page = pageNumber * PageSize;
        if (page < 0 || page + PageSize > document.Length)
            yield break;

        int count = document[page + PageSize - 1];
        if (count == 0 || page + ((count + 1) * 4) + (count * entrySize) > page + PageSize)
            yield break;

        for (int i = 0; i < count; i++)
        {
            int start = BinaryPrimitives.ReadInt32LittleEndian(document.AsSpan(page + (i * 4)));
            int end = BinaryPrimitives.ReadInt32LittleEndian(document.AsSpan(page + ((i + 1) * 4)));
            int wordOffset = document[page + ((count + 1) * 4) + (i * entrySize)];
            if (wordOffset == 0)
            {
                yield return new FormattedRun(start, end, [], -1);
                continue;
            }

            int at = page + (wordOffset * 2);
            if (at < page || at >= page + PageSize)
                continue;

            yield return isParagraph
                ? ReadParagraphProperties(document, page, at, start, end)
                : ReadCharacterProperties(document, page, at, start, end);
        }
    }

    private static FormattedRun ReadParagraphProperties(byte[] document, int page, int at, int start, int end)
    {
        // A paragraph property list stores its length in words, with a zero first byte
        // meaning the real length follows in the next byte.
        int size = document[at];
        int dataOffset = size != 0 ? at + 1 : at + 2;
        int dataLength = size != 0 ? (2 * size) - 1 : 2 * document[at + 1];
        if (dataLength < 2 || dataOffset + dataLength > page + PageSize)
            return new FormattedRun(start, end, [], -1);

        int styleIndex = BinaryPrimitives.ReadUInt16LittleEndian(document.AsSpan(dataOffset));
        return new FormattedRun(start, end, document.AsSpan(dataOffset + 2, dataLength - 2).ToArray(), styleIndex);
    }

    private static FormattedRun ReadCharacterProperties(byte[] document, int page, int at, int start, int end)
    {
        int size = document[at];
        return size == 0 || at + 1 + size > page + PageSize
            ? new FormattedRun(start, end, [], -1)
            : new FormattedRun(start, end, document.AsSpan(at + 1, size).ToArray(), -1);
    }
}
