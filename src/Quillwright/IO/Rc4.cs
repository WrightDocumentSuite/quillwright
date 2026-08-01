namespace Quillwright.IO;

/// <summary>
/// The RC4 stream cipher, which the binary Office formats encrypt with ([MS-OFFCRYPTO] 2.3.5
/// and 2.3.6).
/// </summary>
/// <remarks>
/// RC4 is long broken and no code here ever encrypts with it; it exists so that a document
/// somebody locked twenty years ago can be read today, with a password they supply. The
/// cipher is symmetric, so decrypting is applying the keystream.
/// </remarks>
internal static class Rc4
{
    /// <summary>Applies the keystream of a key to a buffer in place.</summary>
    /// <param name="key">The derived key.</param>
    /// <param name="data">The bytes to transform.</param>
    public static void Apply(ReadOnlySpan<byte> key, Span<byte> data)
    {
        Span<byte> state = stackalloc byte[256];
        for (int i = 0; i < 256; i++)
            state[i] = (byte)i;

        for (int i = 0, j = 0; i < 256; i++)
        {
            j = (j + state[i] + key[i % key.Length]) & 0xFF;
            (state[i], state[j]) = (state[j], state[i]);
        }

        int x = 0;
        int y = 0;
        for (int at = 0; at < data.Length; at++)
        {
            x = (x + 1) & 0xFF;
            y = (y + state[x]) & 0xFF;
            (state[x], state[y]) = (state[y], state[x]);
            data[at] ^= state[(state[x] + state[y]) & 0xFF];
        }
    }
}
