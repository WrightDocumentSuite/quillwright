using System.Buffers.Binary;
using System.Text;

namespace Quillwright.Doc;

/// <summary>One run of characters stored contiguously in the document stream.</summary>
/// <param name="StartPosition">First character position the piece covers.</param>
/// <param name="EndPosition">One past the last character position.</param>
/// <param name="FileOffset">Where the bytes start in the document stream.</param>
/// <param name="IsCompressed">Whether the bytes are single-byte code page text rather than UTF-16.</param>
internal readonly record struct TextPiece(int StartPosition, int EndPosition, int FileOffset, bool IsCompressed)
{
    /// <summary>Number of characters the piece covers.</summary>
    public int Length => EndPosition - StartPosition;
}

/// <summary>
/// The map from character positions to bytes ([MS-DOC] 2.8.35).
/// </summary>
/// <remarks>
/// Word never rewrote a document from the start when you typed in the middle of it. Instead
/// it appended the new text to the file and recorded a new piece pointing at it, so the
/// character order the reader sees is the order of the pieces, not the order of the bytes.
/// Each piece is also independently either single-byte or UTF-16, which is why decoding has
/// to follow the table rather than the stream.
/// </remarks>
internal sealed class PieceTable
{
    private readonly List<TextPiece> _pieces;

    private PieceTable(List<TextPiece> pieces) => _pieces = pieces;

    /// <summary>The pieces, in character order.</summary>
    public IReadOnlyList<TextPiece> Pieces => _pieces;

    /// <summary>Total number of characters described.</summary>
    public int Length => _pieces.Count == 0 ? 0 : _pieces[^1].EndPosition;

    /// <summary>Reads the table out of the complex-file structure in the table stream.</summary>
    /// <param name="table">The table stream.</param>
    /// <param name="offset">Offset of the complex-file structure.</param>
    /// <param name="length">Its length in bytes.</param>
    public static PieceTable Read(byte[] table, int offset, int length)
    {
        int position = offset;
        int limit = Math.Min(offset + length, table.Length);

        // The structure is a sequence of property modifiers followed by the piece table
        // itself; each entry announces its own kind and size.
        while (position < limit)
        {
            byte kind = table[position];
            if (kind == 1)
            {
                if (position + 3 > limit)
                    break;
                position += 3 + BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(position + 1));
                continue;
            }

            if (kind != 2 || position + 5 > limit)
                break;

            int size = BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(position + 1));
            return Parse(table, position + 5, Math.Min(size, limit - position - 5));
        }

        return new PieceTable([]);
    }

    /// <summary>Decodes the characters of a range of positions.</summary>
    /// <param name="document">The <c>WordDocument</c> stream.</param>
    /// <param name="start">First character position.</param>
    /// <param name="end">One past the last character position.</param>
    /// <param name="ansi">Encoding used by single-byte pieces.</param>
    public string ReadText(byte[] document, int start, int end, Encoding ansi)
    {
        var builder = new StringBuilder(Math.Max(0, end - start));
        foreach (TextPiece piece in _pieces)
        {
            if (piece.EndPosition <= start || piece.StartPosition >= end)
                continue;

            int from = Math.Max(start, piece.StartPosition);
            int to = Math.Min(end, piece.EndPosition);
            int skip = from - piece.StartPosition;
            int count = to - from;

            if (piece.IsCompressed)
            {
                int at = piece.FileOffset + skip;
                if (at < 0 || at + count > document.Length)
                    continue;
                builder.Append(ansi.GetString(document, at, count));
            }
            else
            {
                int at = piece.FileOffset + (skip * 2);
                if (at < 0 || at + (count * 2) > document.Length)
                    continue;
                builder.Append(Encoding.Unicode.GetString(document, at, count * 2));
            }
        }

        return builder.ToString();
    }

    /// <summary>The byte offset in the document stream of a character position.</summary>
    /// <param name="position">Character position.</param>
    public int OffsetOf(int position)
    {
        foreach (TextPiece piece in _pieces)
        {
            if (position >= piece.StartPosition && position < piece.EndPosition)
            {
                int skip = position - piece.StartPosition;
                return piece.IsCompressed ? piece.FileOffset + skip : piece.FileOffset + (skip * 2);
            }
        }

        return -1;
    }

    /// <summary>The character position that a byte offset in the document stream belongs to.</summary>
    /// <param name="offset">Byte offset.</param>
    public int PositionOf(int offset)
    {
        foreach (TextPiece piece in _pieces)
        {
            int bytes = piece.IsCompressed ? piece.Length : piece.Length * 2;
            if (offset >= piece.FileOffset && offset < piece.FileOffset + bytes)
            {
                int skip = offset - piece.FileOffset;
                return piece.StartPosition + (piece.IsCompressed ? skip : skip / 2);
            }
        }

        return -1;
    }

    private static PieceTable Parse(byte[] table, int offset, int length)
    {
        // A PLC is n+1 character positions followed by n fixed-size entries; the entry size
        // is what makes the split point computable.
        const int entrySize = 8;
        int count = (length - 4) / (4 + entrySize);
        var pieces = new List<TextPiece>(Math.Max(0, count));

        for (int i = 0; i < count; i++)
        {
            int start = BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(offset + (i * 4)));
            int end = BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(offset + ((i + 1) * 4)));
            int entry = offset + ((count + 1) * 4) + (i * entrySize);
            uint packed = BinaryPrimitives.ReadUInt32LittleEndian(table.AsSpan(entry + 2));

            // Bit 30 says the piece is single-byte, and the offset is then stored quartered.
            bool compressed = (packed & 0x40000000) != 0;
            int fileOffset = compressed ? (int)((packed & 0x3FFFFFFF) / 2) : (int)(packed & 0x3FFFFFFF);
            pieces.Add(new TextPiece(start, end, fileOffset, compressed));
        }

        return new PieceTable(pieces);
    }
}
