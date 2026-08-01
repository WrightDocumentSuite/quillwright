using System.Buffers.Binary;

namespace Quillwright.Doc;

/// <summary>One section of the main story and the page setup that applies to it.</summary>
/// <param name="EndPosition">Character position one past the section's last character.</param>
/// <param name="Properties">The packed section modifiers.</param>
internal readonly record struct DocSection(int EndPosition, byte[] Properties);

/// <summary>
/// Reads the section descriptors ([MS-DOC] 2.8.26, <c>PlcfSed</c>).
/// </summary>
/// <remarks>
/// The descriptors say where each section ends, but not what it looks like: each one holds
/// an offset into the document stream where the section's properties are stored, which is
/// the only structure in the file that points the other way.
/// </remarks>
internal static class DocSectionTable
{
    private const int DescriptorBytes = 12;

    /// <summary>Reads every section of the main story.</summary>
    /// <param name="document">The <c>WordDocument</c> stream.</param>
    /// <param name="table">The table stream.</param>
    /// <param name="offset">Offset of the descriptor list.</param>
    /// <param name="length">Its length in bytes.</param>
    public static List<DocSection> Read(byte[] document, byte[] table, int offset, int length)
    {
        var sections = new List<DocSection>();
        if (length < 4 + DescriptorBytes || offset + length > table.Length)
            return sections;

        int count = (length - 4) / (4 + DescriptorBytes);
        for (int i = 0; i < count; i++)
        {
            int end = BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(offset + ((i + 1) * 4)));
            int descriptor = offset + ((count + 1) * 4) + (i * DescriptorBytes);
            int at = BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(descriptor + 2));

            sections.Add(new DocSection(end, Properties(document, at)));
        }

        return sections;
    }

    private static byte[] Properties(byte[] document, int offset)
    {
        if (offset < 0 || offset + 2 > document.Length)
            return [];

        int size = BinaryPrimitives.ReadUInt16LittleEndian(document.AsSpan(offset));
        return size <= 0 || offset + 2 + size > document.Length
            ? []
            : document.AsSpan(offset + 2, size).ToArray();
    }
}
