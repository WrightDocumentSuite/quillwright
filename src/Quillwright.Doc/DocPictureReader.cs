using System.Buffers.Binary;
using Quillwright.Diagnostics;
using Quillwright.Model;
using Quillwright.Primitives;

namespace Quillwright.Doc;

/// <summary>
/// Reads a picture out of the data stream ([MS-DOC] 2.9.190, <c>PICF</c>, followed by an
/// inline drawing container from [MS-ODRAW]).
/// </summary>
/// <remarks>
/// The text stream holds only a placeholder character; everything about the picture is at an
/// offset in the data stream that the character's properties name. What is stored there is a
/// header describing the frame followed by a drawing container, and the image itself is one
/// record inside it — the same kind of record the document-wide store is made of, so the two
/// are read by one path.
/// </remarks>
internal static class DocPictureReader
{
    private const int HeaderBytes = 68;

    /// <summary>Reads the picture at an offset, or <see langword="null"/> when there is none.</summary>
    /// <param name="data">The data stream.</param>
    /// <param name="offset">Offset the character's properties named.</param>
    /// <param name="loadBudget">Optional counters for decoded image payloads.</param>
    public static Picture? Read(
        byte[] data, int offset, DocumentLoadBudgetState? loadBudget = null)
    {
        if (offset < 0 || offset + HeaderBytes > data.Length)
            return null;

        int total = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset));
        int header = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset + 4));
        if (header < HeaderBytes || total <= header || offset + total > data.Length)
            return null;

        short width = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(offset + 28));
        short height = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(offset + 30));

        ImageData? image = OfficeArtBlip.FindFirst(
            data, offset + header, offset + total, delayed: null, loadBudget);
        return image is null
            ? null
            : new Picture
            {
                Image = image,
                Width = width > 0 ? Length.FromTwips(width) : image.NaturalWidth,
                Height = height > 0 ? Length.FromTwips(height) : image.NaturalHeight,
            };
    }
}
