using System.Buffers.Binary;
using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Doc;

/// <summary>One cell of a row definition.</summary>
/// <param name="Width">Width of the cell, taken from the column boundaries.</param>
/// <param name="StartsHorizontalSpan">Whether this cell begins a run of merged columns.</param>
/// <param name="ContinuesHorizontalSpan">Whether this cell is swallowed by the one before it.</param>
/// <param name="VerticalMerge">How the cell joins the one above it.</param>
/// <param name="VerticalAlignment">Where content sits within the cell.</param>
/// <param name="Shading">The cell's background, when it has one.</param>
internal readonly record struct DocTableCell(
    Length Width,
    bool StartsHorizontalSpan,
    bool ContinuesHorizontalSpan,
    VerticalMerge? VerticalMerge,
    VerticalCellAlignment? VerticalAlignment,
    Shading? Shading = null);

/// <summary>
/// Reads the row definition ([MS-DOC] 2.9.333, <c>TDefTableOperand</c>) that a row mark
/// carries.
/// </summary>
/// <remarks>
/// The definition describes the row as a list of column boundaries plus one entry per grid
/// column. A cell that spans columns is not one wide entry: it is several entries, the first
/// flagged as the start of a span and the rest as swallowed, which is how a reader recovers
/// both the geometry and the merge.
/// </remarks>
internal static class DocTableDefinition
{
    private const int CellBytes = 20;

    /// <summary>Finds and reads the row definition in a paragraph's property list.</summary>
    /// <param name="properties">The property list of a row mark.</param>
    public static List<DocTableCell> Read(ReadOnlySpan<byte> properties)
    {
        List<DocTableCell> cells = [];
        var reader = new SprmReader(properties);
        while (reader.TryRead(out Sprm sprm))
        {
            if (sprm.Opcode == SprmCode.TableDefinition)
                cells = Parse(sprm.Operand);
            else if (sprm.Opcode == SprmCode.TableShading)
                ApplyShading(cells, sprm.Operand);
        }

        return cells;
    }

    /// <summary>
    /// Adds the backgrounds to the cells the definition already described. The two modifiers
    /// are independent, and the shading one may be absent or cover only the first few cells.
    /// </summary>
    private static void ApplyShading(List<DocTableCell> cells, ReadOnlySpan<byte> operand)
    {
        for (int i = 0; i < cells.Count; i++)
        {
            int at = 1 + (i * DocShapes.ShadingBytes);
            if (at + DocShapes.ShadingBytes > operand.Length)
                return;

            cells[i] = cells[i] with { Shading = DocShapes.ReadShading(operand.Slice(at, DocShapes.ShadingBytes)) };
        }
    }

    private static List<DocTableCell> Parse(ReadOnlySpan<byte> operand)
    {
        var cells = new List<DocTableCell>();
        if (operand.Length < 3)
            return cells;

        int columns = operand[2];
        int boundaries = 3 + ((columns + 1) * 2);
        if (columns == 0 || boundaries + (columns * CellBytes) > operand.Length)
            return cells;

        for (int i = 0; i < columns; i++)
        {
            short left = BinaryPrimitives.ReadInt16LittleEndian(operand[(3 + (i * 2))..]);
            short right = BinaryPrimitives.ReadInt16LittleEndian(operand[(3 + ((i + 1) * 2))..]);
            ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(operand[(boundaries + (i * CellBytes))..]);

            cells.Add(new DocTableCell(
                Length.FromTwips(Math.Max(0, right - left)),
                StartsHorizontalSpan: (flags & 0x0001) != 0,
                ContinuesHorizontalSpan: (flags & 0x0002) != 0,
                VerticalMerge: (flags & 0x0040) != 0
                    ? Styles.VerticalMerge.Restart
                    : (flags & 0x0020) != 0 ? Styles.VerticalMerge.Continue : null,
                VerticalAlignment: ((flags >> 7) & 0x0003) switch
                {
                    1 => VerticalCellAlignment.Center,
                    2 => VerticalCellAlignment.Bottom,
                    _ => null,
                }));
        }

        return cells;
    }
}
