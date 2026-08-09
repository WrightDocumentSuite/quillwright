using Quillwright.Model;
using Quillwright.Diagnostics;

namespace Quillwright.Doc;

/// <summary>
/// The images of a whole document, kept once and referred to by position ([MS-ODRAW] 2.2.20,
/// <c>OfficeArtBStoreContainer</c>).
/// </summary>
/// <remarks>
/// A floating shape does not carry its picture; it carries a number, and the picture is the
/// one at that place in this list. The list is therefore read once per file and the images in
/// it are decoded only when a shape actually asks for one, because a document can keep images
/// no shape displays any more.
/// </remarks>
internal sealed class OfficeArtBlipStore
{
    /// <summary>The container for everything the whole document draws.</summary>
    private const ushort DrawingGroup = 0xF000;

    /// <summary>The container for the images inside it.</summary>
    private const ushort ImageStore = 0xF001;

    private readonly byte[] _table;
    private readonly byte[]? _delayed;
    private readonly OfficeArtRecord[] _entries;
    private readonly ImageData?[] _images;
    private readonly DocumentLoadBudgetState? _loadBudget;

    private OfficeArtBlipStore(
        byte[] table, byte[]? delayed, OfficeArtRecord[] entries, DocumentLoadBudgetState? loadBudget)
    {
        _table = table;
        _delayed = delayed;
        _entries = entries;
        _images = new ImageData?[entries.Length];
        _loadBudget = loadBudget;
    }

    /// <summary>A document that draws nothing.</summary>
    public static OfficeArtBlipStore Empty { get; } = new([], null, [], null);

    /// <summary>How many images the document keeps.</summary>
    public int Count => _entries.Length;

    /// <summary>Reads the store of a document.</summary>
    /// <param name="table">The table stream.</param>
    /// <param name="region">Where the drawings live, and how long they are.</param>
    /// <param name="delayed">The stream images may have been left in, rather than stored inline.</param>
    /// <param name="loadBudget">Optional counters for decoded image payloads.</param>
    public static OfficeArtBlipStore Read(
        byte[] table,
        (int Offset, int Length) region,
        byte[]? delayed,
        DocumentLoadBudgetState? loadBudget = null)
    {
        (int offset, int length) = region;
        if (length <= 0 || offset < 0 || offset + length > table.Length)
            return Empty;

        int end = offset + length;
        if (OfficeArtRecord.Find(table, offset, end, DrawingGroup) is not { } group)
            return Empty;

        OfficeArtRecord? store = OfficeArtRecord.Find(table, group.Body, group.End, ImageStore);
        OfficeArtRecord[] entries = store is { } found
            ? [.. found.Children(table).Where(static r => OfficeArtBlip.IsEntry(r.Type))]
            : [];

        return new OfficeArtBlipStore(table, delayed, entries, loadBudget);
    }

    /// <summary>The image a shape displays, wherever it keeps it.</summary>
    /// <param name="shape">The shape.</param>
    public ImageData? For(OfficeArtShape shape) =>
        shape.ImageOffset >= 0 ? Inline(shape.ImageOffset) : At(shape.ImageIndex);

    /// <summary>The image at a place in the list, counted from one as the shapes count it.</summary>
    /// <param name="index">One-based position in the list.</param>
    public ImageData? At(int index)
    {
        if (index < 1 || index > _entries.Length)
            return null;

        return _images[index - 1] ??= OfficeArtBlip.Resolve(
            _table, _entries[index - 1], _delayed, _loadBudget);
    }

    /// <summary>An image a shape carried itself rather than taking from the list.</summary>
    private ImageData? Inline(int offset) =>
        OfficeArtRecord.TryRead(_table, offset, _table.Length, out OfficeArtRecord record)
            ? OfficeArtBlip.Resolve(_table, record, _delayed, _loadBudget)
            : null;
}
