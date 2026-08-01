using System.Buffers.Binary;
using Quillwright.Model;

namespace Quillwright.Doc.Writing;

/// <summary>
/// Builds the document properties ([MS-DOC] 2.7.6, <c>Dop97</c>) - the block of settings and
/// counters that applies to the file as a whole.
/// </summary>
/// <remarks>
/// The block is hundreds of bytes of compatibility flags and statistics, nearly all of which
/// a reader is free to ignore, but it is not optional: a file without one is rejected. Its
/// size is how a reader tells which version wrote it, so it has to match the version the
/// header claims. Only the fields that change what a reader does are set; the rest are left
/// at zero, which is the documented default for each of them.
/// </remarks>
internal static class DopBuilder
{
    private const int Size = 694;

    /// <summary>Position of the first flag word, whose lowest bit asks for facing pages.</summary>
    private const int Flags = 0;

    /// <summary>Position of the default tab stop interval.</summary>
    private const int DefaultTabStop = 10;

    /// <summary>Position of the character count.</summary>
    private const int CharacterCount = 42;

    /// <summary>Position of the paragraph count.</summary>
    private const int ParagraphCount = 48;

    /// <summary>Writes the properties of a document.</summary>
    /// <param name="document">The document being written.</param>
    public static byte[] Build(WordDocument document)
    {
        var bytes = new byte[Size];
        Span<byte> span = bytes;

        // The lowest bit of the first word is fFacingPages, which is this format's name for
        // "even and odd pages have different headers". Setting it on a document that has only
        // one header tells Word the even pages have none.
        BinaryPrimitives.WriteUInt16LittleEndian(span[Flags..], HasFacingPages(document) ? (ushort)1 : (ushort)0);
        BinaryPrimitives.WriteUInt16LittleEndian(span[DefaultTabStop..], (ushort)Math.Clamp(document.Settings.DefaultTabStop.Twips, 1, ushort.MaxValue));

        BinaryPrimitives.WriteInt32LittleEndian(span[CharacterCount..], Count(document, static block => block.GetText().Length));
        BinaryPrimitives.WriteInt32LittleEndian(span[ParagraphCount..], Math.Max(1, Count(document, static block => block is Paragraph ? 1 : 0)));
        return bytes;
    }

    /// <summary>
    /// Whether the document asks for different headers on even and odd pages, either because
    /// it says so or because some section actually has an even-page header or footer.
    /// </summary>
    private static bool HasFacingPages(WordDocument document) =>
        document.Settings.EvenAndOddHeaders ||
        document.Sections.Any(static section =>
            section.Headers.Even is { Blocks.Count: > 0 } || section.Footers.Even is { Blocks.Count: > 0 });

    private static int Count(WordDocument document, Func<Block, int> measure) =>
        document.Sections.SelectMany(static section => section.Blocks).Sum(measure);
}
