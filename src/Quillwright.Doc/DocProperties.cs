using System.Buffers.Binary;
using Quillwright.Model;
using Quillwright.Primitives;

namespace Quillwright.Doc;

/// <summary>
/// Reads the document properties ([MS-DOC] 2.7.6, <c>Dop</c>) — the settings that apply to
/// the file rather than to any part of it.
/// </summary>
/// <remarks>
/// Only the few fields that change what a reader shows are taken. The most consequential is
/// the lowest bit of the first word: this format's name for it is <c>fFacingPages</c>, and it
/// is what decides whether the even-page headers and footers are used at all.
/// </remarks>
internal static class DocProperties
{
    private const int DefaultTabStop = 10;

    /// <summary>Applies the document properties to a document's settings.</summary>
    /// <param name="table">The table stream.</param>
    /// <param name="offset">Where the properties live.</param>
    /// <param name="length">How long they are.</param>
    /// <param name="settings">The settings to update in place.</param>
    public static void Apply(byte[] table, int offset, int length, DocumentSettings settings)
    {
        if (length < DefaultTabStop + 2 || offset + length > table.Length)
            return;

        if ((BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(offset)) & 0x0001) != 0)
            settings.EvenAndOddHeaders = true;

        int tab = BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(offset + DefaultTabStop));
        if (tab > 0)
            settings.DefaultTabStop = Length.FromTwips(tab);
    }
}
