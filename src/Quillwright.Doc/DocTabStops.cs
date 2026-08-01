using System.Buffers.Binary;
using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Doc;

/// <summary>
/// Reads and writes custom tab stops ([MS-DOC] 2.9.190, <c>PChgTabsPapxOperand</c>).
/// </summary>
/// <remarks>
/// The operand is two lists rather than one: the stops a paragraph adds, and the positions at
/// which it ignores a stop it would otherwise inherit from its style. The newer format spells
/// the second kind as a stop whose alignment is <c>clear</c>, so the two lists are split apart
/// on the way out and joined back together on the way in.
/// </remarks>
internal static class DocTabStops
{
    private const int MaximumStops = 64;
    private const int MaximumOperand = 255;

    /// <summary>Builds the operand for a paragraph's tab stops, or nothing when it has none.</summary>
    /// <param name="tabs">The stops to encode.</param>
    public static byte[] Build(IReadOnlyList<TabStop> tabs)
    {
        List<TabStop> cleared = [.. Ordered(tabs.Where(static tab => tab.Alignment == TabAlignment.Clear))];
        List<TabStop> added = [.. Ordered(tabs.Where(static tab => tab.Alignment != TabAlignment.Clear))];
        if (cleared.Count == 0 && added.Count == 0)
            return [];

        // The whole operand has to fit in one byte's worth of length, and each list in six
        // bits' worth of count, so the stops beyond that are dropped rather than truncating
        // the structure halfway through.
        while (1 + (cleared.Count * 4) + 1 + (added.Count * 3) > MaximumOperand)
        {
            if (cleared.Count > added.Count)
                cleared.RemoveAt(cleared.Count - 1);
            else
                added.RemoveAt(added.Count - 1);
        }

        var bytes = new List<byte>(2 + (cleared.Count * 4) + (added.Count * 3));
        bytes.Add((byte)cleared.Count);
        foreach (TabStop tab in cleared)
            Append(bytes, Position(tab));

        // Each ignored position carries the distance either side of it that the rule covers.
        foreach (TabStop _ in cleared)
            Append(bytes, 0);

        bytes.Add((byte)added.Count);
        foreach (TabStop tab in added)
            Append(bytes, Position(tab));
        foreach (TabStop tab in added)
            bytes.Add((byte)(AlignmentCode(tab.Alignment) | (LeaderCode(tab.Leader) << 3)));

        return [.. bytes];
    }

    /// <summary>
    /// Reads the operand back into stops. The operand begins with its own size, which the
    /// property list already accounts for.
    /// </summary>
    /// <param name="operand">The modifier's operand, size byte included.</param>
    public static EquatableArray<TabStop> Read(ReadOnlySpan<byte> operand)
    {
        if (operand.Length < 3)
            return default;

        var tabs = new List<TabStop>();
        int position = 1;

        int cleared = operand[position++];
        if (cleared > MaximumStops || position + (cleared * 4) + 1 > operand.Length)
            return default;

        for (int i = 0; i < cleared; i++)
        {
            short at = BinaryPrimitives.ReadInt16LittleEndian(operand[(position + (i * 2))..]);
            tabs.Add(new TabStop(Length.FromTwips(at), TabAlignment.Clear));
        }

        position += cleared * 4;
        int added = operand[position++];
        if (added > MaximumStops || position + (added * 3) > operand.Length)
            return tabs.Count == 0 ? default : new EquatableArray<TabStop>([.. tabs]);

        for (int i = 0; i < added; i++)
        {
            short at = BinaryPrimitives.ReadInt16LittleEndian(operand[(position + (i * 2))..]);
            byte descriptor = operand[position + (added * 2) + i];
            tabs.Add(new TabStop(
                Length.FromTwips(at),
                Alignment(descriptor & 0x07),
                Leader((descriptor >> 3) & 0x07)));
        }

        tabs.Sort(static (left, right) => left.Position.Twips.CompareTo(right.Position.Twips));
        return tabs.Count == 0 ? default : new EquatableArray<TabStop>([.. tabs]);
    }

    /// <summary>Both lists must ascend, and neither may hold more stops than the count allows.</summary>
    private static IEnumerable<TabStop> Ordered(IEnumerable<TabStop> tabs) =>
        tabs.OrderBy(static tab => tab.Position.Twips).Take(MaximumStops);

    private static short Position(TabStop tab) => (short)Math.Clamp(tab.Position.Twips, -31680, 31680);

    private static void Append(List<byte> bytes, short value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteInt16LittleEndian(buffer, value);
        bytes.AddRange(buffer);
    }

    private static int AlignmentCode(TabAlignment alignment) => alignment switch
    {
        TabAlignment.Center => 1,
        TabAlignment.Right => 2,
        TabAlignment.Decimal => 3,
        TabAlignment.Bar => 4,
        _ => 0,
    };

    private static TabAlignment Alignment(int code) => code switch
    {
        1 => TabAlignment.Center,
        2 => TabAlignment.Right,
        3 => TabAlignment.Decimal,
        4 => TabAlignment.Bar,
        _ => TabAlignment.Left,
    };

    private static int LeaderCode(TabLeader leader) => leader switch
    {
        TabLeader.Dot => 1,
        TabLeader.Hyphen => 2,
        TabLeader.Underscore => 3,
        TabLeader.Heavy => 4,
        TabLeader.MiddleDot => 5,
        _ => 0,
    };

    private static TabLeader Leader(int code) => code switch
    {
        1 => TabLeader.Dot,
        2 => TabLeader.Hyphen,
        3 => TabLeader.Underscore,
        4 => TabLeader.Heavy,
        5 => TabLeader.MiddleDot,
        _ => TabLeader.None,
    };
}
