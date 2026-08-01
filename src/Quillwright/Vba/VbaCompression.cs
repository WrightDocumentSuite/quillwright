namespace Quillwright.Vba;

/// <summary>
/// Unpacks the compressed container that VBA stores its source in ([MS-OVBA] 2.4.1).
/// </summary>
/// <remarks>
/// <para>
/// The container is a signature byte followed by chunks. Each chunk carries a two-byte header
/// giving its length and whether it is compressed, and expands to at most 4096 bytes. A
/// compressed chunk is a run of token sequences: a flag byte, then eight tokens, each either a
/// literal byte or a two-byte reference back into what has already been produced.
/// </para>
/// <para>
/// The split of a reference into offset and length is not fixed — it depends on how much of the
/// current chunk has been decoded so far, so that early references, which cannot point far back,
/// spend fewer bits on the offset and more on the length. A reference may also reach into bytes
/// it is itself producing, which is how the format expresses a repeated run.
/// </para>
/// <para>
/// Damaged input is truncated rather than refused: a partly readable macro is more use than an
/// exception, and this code exists to look inside files of unknown provenance.
/// </para>
/// </remarks>
internal static class VbaCompression
{
    private const byte ContainerSignature = 0x01;
    private const int ChunkSignature = 0b011;
    private const int MaximumChunk = 4096;

    /// <summary>Expands a compressed container.</summary>
    /// <param name="compressed">The container, starting at its signature byte.</param>
    /// <returns>The bytes it holds, empty when the input is not a container.</returns>
    public static byte[] Decompress(ReadOnlySpan<byte> compressed)
    {
        if (compressed.Length < 3 || compressed[0] != ContainerSignature)
            return [];

        var output = new List<byte>(compressed.Length * 4);
        int position = 1;

        while (position + 1 < compressed.Length)
        {
            int header = compressed[position] | (compressed[position + 1] << 8);
            position += 2;

            if ((header >> 12 & 0x07) != ChunkSignature)
                break;

            // The recorded size counts the header itself and is stored three short.
            int end = Math.Min(compressed.Length, position + (header & 0x0FFF) + 1);
            if ((header & 0x8000) == 0)
            {
                output.AddRange(compressed[position..end]);
                position = end;
                continue;
            }

            ExpandChunk(compressed[position..end], output);
            position = end;
        }

        return [.. output];
    }

    /// <summary>Whether bytes plausibly begin a compressed container.</summary>
    /// <param name="data">The bytes to test.</param>
    public static bool LooksLikeContainer(ReadOnlySpan<byte> data) =>
        data.Length >= 3 &&
        data[0] == ContainerSignature &&
        ((data[1] | (data[2] << 8)) >> 12 & 0x07) == ChunkSignature;

    /// <summary>Decodes the token sequences of one compressed chunk.</summary>
    /// <param name="chunk">Chunk data with the header already consumed.</param>
    /// <param name="output">Where decoded bytes accumulate, across chunks.</param>
    private static void ExpandChunk(ReadOnlySpan<byte> chunk, List<byte> output)
    {
        int start = output.Count;
        int position = 0;

        while (position < chunk.Length)
        {
            byte flags = chunk[position++];
            for (int token = 0; token < 8 && position < chunk.Length; token++)
            {
                if ((flags & (1 << token)) == 0)
                {
                    output.Add(chunk[position++]);
                    continue;
                }

                if (position + 1 >= chunk.Length)
                    return;

                int reference = chunk[position] | (chunk[position + 1] << 8);
                position += 2;
                if (!CopyBack(reference, output, start))
                    return;
            }
        }
    }

    /// <summary>Copies a run that a reference token points back at.</summary>
    /// <param name="reference">The two bytes of the token.</param>
    /// <param name="output">Where decoded bytes accumulate.</param>
    /// <param name="start">Where the current chunk began in <paramref name="output"/>.</param>
    /// <returns><see langword="false"/> when the token points outside what has been decoded.</returns>
    private static bool CopyBack(int reference, List<byte> output, int start)
    {
        int bits = OffsetBits(output.Count - start);
        int lengthMask = 0xFFFF >> bits;
        int length = (reference & lengthMask) + 3;
        int offset = ((reference & ~lengthMask & 0xFFFF) >> (16 - bits)) + 1;

        int from = output.Count - offset;
        if (from < 0 || output.Count - start + length > MaximumChunk)
            return false;

        // Reads may overlap what this loop writes, which is how a repeated run is encoded.
        for (int i = 0; i < length; i++)
            output.Add(output[from + i]);

        return true;
    }

    /// <summary>How many of a token's sixteen bits hold the offset, given what is behind it.</summary>
    /// <param name="decoded">Bytes decoded so far in the current chunk.</param>
    private static int OffsetBits(int decoded)
    {
        int bits = 4;
        while (bits < 12 && 1 << bits < decoded)
            bits++;

        return bits;
    }
}
