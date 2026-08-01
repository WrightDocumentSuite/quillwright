using System.Text;
using Quillwright.Vba;

namespace Quillwright.Tests;

/// <summary>
/// Checks the compressed container against the worked examples in [MS-OVBA] section 3.2.
/// </summary>
/// <remarks>
/// These are the specification's own byte arrays, which makes them the one check on this code
/// that owes nothing to Word, to a fixture, or to an encoder of ours. Between them they cover
/// the three shapes a chunk can take: stored whole, a mixture of literals and back-references,
/// and a single reference that overlaps the bytes it is producing.
/// </remarks>
public class VbaCompressionTests
{
    /// <summary>[MS-OVBA] 3.2.1 — text with nothing worth referencing back to.</summary>
    [Fact]
    public void TheNoCompressionExample_Expands()
    {
        byte[] compressed = Bytes("01 19 B0 00 61 62 63 64 65 66 67 68 00 69 6A 6B 6C 6D 6E 6F 70 00 71 72 73 74 75 76 2E");

        Assert.Equal("abcdefghijklmnopqrstuv.", Text(compressed));
    }

    /// <summary>[MS-OVBA] 3.2.2 — a typical mixture of literals and back-references.</summary>
    [Fact]
    public void TheNormalCompressionExample_Expands()
    {
        byte[] compressed = Bytes(
            "01 2F B0 00 23 61 61 61 62 63 64 65 82 66 00 70 " +
            "61 67 68 69 6A 01 38 08 61 6B 6C 00 20 6D 6E 6F " +
            "70 06 71 02 70 04 00 72 73 74 75 76 10 77 78 79 " +
            "7A 00 2C");

        Assert.Equal("#aaabcdefaaaaghijaaaaaklaaamnopqaaaaaaaaaaaarstuvwxyzaaa", Text(compressed));
    }

    /// <summary>
    /// [MS-OVBA] 3.2.3 — seventy-three identical bytes from one literal and one reference of
    /// length seventy-two at offset one, which only works if a copy may read what it writes.
    /// </summary>
    [Fact]
    public void TheMaximumCompressionExample_Expands()
    {
        byte[] compressed = Bytes("01 03 B0 02 61 45 00");

        Assert.Equal(new string('a', 73), Text(compressed));
    }

    /// <summary>The decompressed bytes of every example match the specification exactly.</summary>
    [Fact]
    public void TheExamples_MatchTheirDecompressedBytes()
    {
        Assert.Equal(
            Bytes("61 62 63 64 65 66 67 68 69 6A 6B 6C 6D 6E 6F 70 71 72 73 74 75 76 2E"),
            VbaCompression.Decompress(Bytes("01 19 B0 00 61 62 63 64 65 66 67 68 00 69 6A 6B 6C 6D 6E 6F 70 00 71 72 73 74 75 76 2E")));

        Assert.Equal(
            Bytes(
                "23 61 61 61 62 63 64 65 66 61 61 61 61 67 68 69 " +
                "6A 61 61 61 61 61 6B 6C 61 61 61 6D 6E 6F 70 71 " +
                "61 61 61 61 61 61 61 61 61 61 61 61 72 73 74 75 " +
                "76 77 78 79 7A 61 61 61"),
            VbaCompression.Decompress(Bytes(
                "01 2F B0 00 23 61 61 61 62 63 64 65 82 66 00 70 " +
                "61 67 68 69 6A 01 38 08 61 6B 6C 00 20 6D 6E 6F " +
                "70 06 71 02 70 04 00 72 73 74 75 76 10 77 78 79 " +
                "7A 00 2C")));
    }

    /// <summary>
    /// A chunk that would not shrink is stored whole instead, which none of the specification's
    /// examples happen to do — all three of those are compressed chunks. Several of them in a
    /// row also checks that chunks are walked rather than only the first one read.
    /// </summary>
    [Fact]
    public void ChunksStoredWithoutCompression_AreCopiedThrough()
    {
        var text = new StringBuilder();
        while (text.Length < 5 * 4096)
            text.Append("Public Sub Repeated()\r\n    Debug.Print \"x\"\r\nEnd Sub\r\n");

        byte[] expected = Encoding.ASCII.GetBytes(text.ToString()[..(5 * 4096)]);
        byte[] container = StoreUncompressed(expected);

        Assert.Equal(1 + (5 * 4098), container.Length);
        Assert.Equal(expected, VbaCompression.Decompress(container));
    }

    [Fact]
    public void BytesThatAreNotAContainer_DecompressToNothing()
    {
        Assert.Empty(VbaCompression.Decompress([0x02, 0x00, 0x00, 0x00]));
        Assert.Empty(VbaCompression.Decompress([]));
        Assert.False(VbaCompression.LooksLikeContainer([0x01, 0x00, 0x00]));
        Assert.True(VbaCompression.LooksLikeContainer([0x01, 0x00, 0xB0]));
    }

    /// <summary>A container that stops mid-chunk yields what it managed rather than throwing.</summary>
    [Theory]
    [InlineData(7)]
    [InlineData(20)]
    [InlineData(40)]
    public void ATruncatedContainer_YieldsWhatItManaged(int keep)
    {
        byte[] whole = Bytes(
            "01 2F B0 00 23 61 61 61 62 63 64 65 82 66 00 70 " +
            "61 67 68 69 6A 01 38 08 61 6B 6C 00 20 6D 6E 6F " +
            "70 06 71 02 70 04 00 72 73 74 75 76 10 77 78 79 " +
            "7A 00 2C");

        string partial = Text(whole[..keep]);

        Assert.StartsWith(partial, "#aaabcdefaaaaghijaaaaaklaaamnopqaaaaaaaaaaaarstuvwxyzaaa", StringComparison.Ordinal);
    }

    /// <summary>
    /// Wraps bytes in stored chunks. A stored chunk always holds exactly 4096 bytes, so the
    /// input has to be a whole number of them.
    /// </summary>
    /// <param name="data">The bytes to store, a multiple of 4096 in length.</param>
    private static byte[] StoreUncompressed(ReadOnlySpan<byte> data)
    {
        // Signature 0b011 in bits 12-14, the compression flag clear, and the mandatory 4095.
        const int header = 0x3000 | 4095;

        var container = new List<byte> { 0x01 };
        for (int at = 0; at < data.Length; at += 4096)
        {
            container.Add((byte)(header & 0xFF));
            container.Add((byte)(header >> 8));
            container.AddRange(data.Slice(at, 4096));
        }

        return [.. container];
    }

    private static string Text(ReadOnlySpan<byte> compressed) =>
        Encoding.ASCII.GetString(VbaCompression.Decompress(compressed));

    private static byte[] Bytes(string hex) => Convert.FromHexString(hex.Replace(" ", string.Empty, StringComparison.Ordinal));
}
