using System.Buffers.Binary;

namespace Quillwright.Doc;

/// <summary>
/// The positions of the anchors that tie the stories following the main text back to it.
/// </summary>
/// <remarks>
/// Nothing in a note or a comment says where it belongs. The link is two parallel lists: one
/// of positions in the main text where a reference character sits, one of positions in the
/// note story where each body begins, paired by index. Reading either alone is useless.
/// </remarks>
internal sealed class DocStoryReader
{
    private DocStoryReader(List<int> references, List<(int Start, int End)> bodies, List<byte[]> records)
    {
        References = references;
        Bodies = bodies;
        Records = records;
    }

    /// <summary>An empty set of anchors.</summary>
    public static DocStoryReader Empty { get; } = new([], [], []);

    /// <summary>Positions in the main text where a reference character sits.</summary>
    public IReadOnlyList<int> References { get; }

    /// <summary>Ranges of the note story that each body occupies.</summary>
    public IReadOnlyList<(int Start, int End)> Bodies { get; }

    /// <summary>The record stored against each reference, parallel to <see cref="References"/>.</summary>
    public IReadOnlyList<byte[]> Records { get; }

    /// <summary>Reads one story's reference and body lists.</summary>
    /// <param name="table">The table stream.</param>
    /// <param name="reference">Where the reference list lives, and how long it is.</param>
    /// <param name="body">Where the body list lives, and how long it is.</param>
    /// <param name="recordBytes">Size of the record that follows each reference position.</param>
    public static DocStoryReader Read(byte[] table, (int Offset, int Length) reference, (int Offset, int Length) body, int recordBytes)
    {
        List<int> references = Positions(table, reference.Offset, reference.Length, recordBytes);
        List<int> boundaries = Positions(table, body.Offset, body.Length, 0);

        // The reference list ends with one position that is not a reference, and the body
        // list with two that are not bodies.
        if (references.Count > 0)
            references.RemoveAt(references.Count - 1);

        var bodies = new List<(int Start, int End)>();
        for (int i = 0; i + 2 < boundaries.Count; i++)
            bodies.Add((boundaries[i], boundaries[i + 1]));

        return new DocStoryReader(references, bodies, ReadRecords(table, reference, references.Count, recordBytes));
    }

    /// <summary>Reads the fixed-size record stored against each position of a list.</summary>
    private static List<byte[]> ReadRecords(byte[] table, (int Offset, int Length) region, int count, int recordBytes)
    {
        var records = new List<byte[]>(count);
        if (recordBytes <= 0)
            return records;

        int start = region.Offset + ((count + 1) * 4);
        for (int i = 0; i < count; i++)
        {
            int at = start + (i * recordBytes);
            if (at + recordBytes > region.Offset + region.Length || at + recordBytes > table.Length)
                break;
            records.Add(table.AsSpan(at, recordBytes).ToArray());
        }

        return records;
    }

    /// <summary>Reads a list that is nothing but positions, such as the header story boundaries.</summary>
    /// <param name="table">The table stream.</param>
    /// <param name="offset">Where the list lives.</param>
    /// <param name="length">How long it is.</param>
    public static List<int> ReadPositions(byte[] table, int offset, int length)
    {
        var positions = new List<int>();
        if (length < 8 || offset + length > table.Length)
            return positions;

        for (int i = 0; (i + 1) * 4 <= length; i++)
            positions.Add(BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(offset + (i * 4))));

        return positions;
    }

    private static List<int> Positions(byte[] table, int offset, int length, int recordBytes)
    {
        var positions = new List<int>();
        if (length < 8 || offset + length > table.Length)
            return positions;

        int count = recordBytes == 0 ? length / 4 : ((length - 4) / (4 + recordBytes)) + 1;
        for (int i = 0; i < count && (i + 1) * 4 <= length; i++)
            positions.Add(BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(offset + (i * 4))));

        return positions;
    }
}
