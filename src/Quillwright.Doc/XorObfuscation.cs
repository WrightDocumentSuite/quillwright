using System.Text;

namespace Quillwright.Doc;

/// <summary>
/// Unscrambles a document protected by XOR obfuscation ([MS-OFFCRYPTO] 2.3.7, applied as
/// [MS-DOC] 2.2.6.1).
/// </summary>
/// <remarks>
/// <para>
/// This is the oldest of the three schemes and is not encryption in any useful sense: the
/// password derives a sixteen-byte pad, and every byte of the three content streams is
/// exclusive-ored with the pad byte at its own position. What makes it a scheme at all rather
/// than a rotation is the rule that a byte is left alone when it is zero or when the result
/// would be, which is what keeps the stream's own structure legible to a reader looking for
/// it.
/// </para>
/// <para>
/// Only reading is offered. A document opened this way is written back either unlocked or
/// behind a scheme worth the name.
/// </para>
/// </remarks>
internal static class XorObfuscation
{
    /// <summary>Bytes of the document stream that stay readable however the file is locked.</summary>
    private const int PlainPrefix = 68;

    /// <summary>Longest password the scheme takes ([MS-OFFCRYPTO] 2.3.7.5).</summary>
    private const int MaxPasswordBytes = 15;

    private static ReadOnlySpan<byte> Pad =>
        [0xBB, 0xFF, 0xFF, 0xBA, 0xFF, 0xFF, 0xB9, 0x80, 0x00, 0xBE, 0x0F, 0x00, 0xBF, 0x0F, 0x00];

    private static ReadOnlySpan<ushort> InitialCode =>
    [
        0xE1F0, 0x1D0F, 0xCC9C, 0x84C0, 0x110C, 0x0E10, 0xF1CE, 0x313E,
        0x1872, 0xE139, 0xD40F, 0x84F9, 0x280C, 0xA96A, 0x4EC3,
    ];

    private static ReadOnlySpan<ushort> Matrix =>
    [
        0xAEFC, 0x4DD9, 0x9BB2, 0x2745, 0x4E8A, 0x9D14, 0x2A09,
        0x7B61, 0xF6C2, 0xFDA5, 0xEB6B, 0xC6F7, 0x9DCF, 0x2BBF,
        0x4563, 0x8AC6, 0x05AD, 0x0B5A, 0x16B4, 0x2D68, 0x5AD0,
        0x0375, 0x06EA, 0x0DD4, 0x1BA8, 0x3750, 0x6EA0, 0xDD40,
        0xD849, 0xA0B3, 0x5147, 0xA28E, 0x553D, 0xAA7A, 0x44D5,
        0x6F45, 0xDE8A, 0xAD35, 0x4A4B, 0x9496, 0x390D, 0x721A,
        0xEB23, 0xC667, 0x9CEF, 0x29FF, 0x53FE, 0xA7FC, 0x5FD9,
        0x47D3, 0x8FA6, 0x0F6D, 0x1EDA, 0x3DB4, 0x7B68, 0xF6D0,
        0xB861, 0x60E3, 0xC1C6, 0x93AD, 0x377B, 0x6EF6, 0xDDEC,
        0x45A0, 0x8B40, 0x06A1, 0x0D42, 0x1A84, 0x3508, 0x6A10,
        0xAA51, 0x4483, 0x8906, 0x022D, 0x045A, 0x08B4, 0x1168,
        0x76B4, 0xED68, 0xCAF1, 0x85C3, 0x1BA7, 0x374E, 0x6E9C,
        0x3730, 0x6E60, 0xDCC0, 0xA9A1, 0x4363, 0x86C6, 0x1DAD,
        0x3331, 0x6662, 0xCCC4, 0x89A9, 0x0373, 0x06E6, 0x0DCC,
        0x1021, 0x2042, 0x4084, 0x8108, 0x1231, 0x2462, 0x48C4,
    ];

    /// <summary>
    /// Whether a password matches the verifier the header stores. Both ways of narrowing a
    /// Unicode password to single bytes are tried, because the specification says a file may
    /// have been written either way and a reader has to accept both (2.3.7.4).
    /// </summary>
    /// <param name="password">The password to try.</param>
    /// <param name="stored">The verifier from <c>FibBase.lKey</c>.</param>
    public static bool Verify(string password, uint stored) =>
        Narrowings(password).Any(bytes => Verifier(bytes) == stored);

    /// <summary>Unscrambles the three streams a protected document keeps its content in.</summary>
    /// <param name="document">The <c>WordDocument</c> stream.</param>
    /// <param name="table">The table stream.</param>
    /// <param name="data">The data stream, which may be empty.</param>
    /// <param name="password">The password, already checked against the verifier.</param>
    /// <param name="stored">The verifier from <c>FibBase.lKey</c>, which says which narrowing was used.</param>
    public static (byte[] Document, byte[] Table, byte[] Data) Decode(
        byte[] document, byte[] table, byte[] data, string password, uint stored)
    {
        byte[] narrowed = Narrowings(password).FirstOrDefault(bytes => Verifier(bytes) == stored)
            ?? Narrowings(password)[0];
        byte[] pad = Array(narrowed);

        return (Transform(document, pad, PlainPrefix), Transform(table, pad, 0), Transform(data, pad, 0));
    }

    /// <summary>
    /// Exclusive-ors a stream against the pad, leaving alone the bytes the scheme leaves
    /// alone ([MS-OFFCRYPTO] 2.3.7.6).
    /// </summary>
    private static byte[] Transform(byte[] stream, byte[] pad, int plain)
    {
        var result = new byte[stream.Length];
        stream.CopyTo(result, 0);

        for (int at = plain; at < stream.Length; at++)
        {
            byte value = result[at];
            if (value == 0)
                continue;

            byte transformed = (byte)(value ^ pad[at % pad.Length]);
            if (transformed != 0)
                result[at] = transformed;
        }

        return result;
    }

    /// <summary>The sixteen-byte pad the password derives ([MS-OFFCRYPTO] 2.3.7.5).</summary>
    private static byte[] Array(byte[] password)
    {
        ushort key = XorKey(password);
        byte high = (byte)(key >> 8);
        byte low = (byte)key;

        var array = new byte[16];
        for (int i = 0; i < array.Length; i++)
            array[i] = i < password.Length ? password[i] : Pad[i - password.Length];

        for (int i = 0; i < array.Length; i += 2)
        {
            array[i] = Ror((byte)(array[i] ^ low));
            array[i + 1] = Ror((byte)(array[i + 1] ^ high));
        }

        return array;
    }

    /// <summary>The thirty-two-bit verifier the header stores ([MS-OFFCRYPTO] 2.3.7.4).</summary>
    private static uint Verifier(byte[] password) => ((uint)XorKey(password) << 16) | ShortVerifier(password);

    /// <summary>The high half, folded out of the password through a fixed table (2.3.7.2).</summary>
    private static ushort XorKey(byte[] password)
    {
        if (password.Length == 0)
            return 0;

        ushort key = InitialCode[password.Length - 1];
        int element = 0x68;
        for (int i = password.Length - 1; i >= 0; i--)
        {
            int character = password[i];
            for (int bit = 0; bit < 7 && element >= 0; bit++, element--)
            {
                if ((character & 0x40) != 0)
                    key ^= Matrix[element];
                character <<= 1;
            }
        }

        return key;
    }

    /// <summary>The low half, a rotating exclusive-or over the password and its length (2.3.7.1).</summary>
    private static ushort ShortVerifier(byte[] password)
    {
        ushort verifier = 0;
        for (int i = password.Length; i >= 0; i--)
        {
            byte value = i == 0 ? (byte)password.Length : password[i - 1];
            int carried = (verifier & 0x4000) == 0 ? 0 : 1;
            verifier = (ushort)((((verifier << 1) & 0x7FFF) | carried) ^ value);
        }

        return (ushort)(verifier ^ 0xCE4B);
    }

    /// <summary>
    /// The password as single bytes, both ways the specification allows: the code page it was
    /// typed in, and the low byte of each character.
    /// </summary>
    private static byte[][] Narrowings(string password)
    {
        var low = new byte[Math.Min(password.Length, MaxPasswordBytes)];
        for (int i = 0; i < low.Length; i++)
            low[i] = password[i] is var c && (byte)c != 0 ? (byte)c : (byte)(c >> 8);

        byte[] ansi = Truncate(Encoding.Latin1.GetBytes(password));
        return ansi.AsSpan().SequenceEqual(low) ? [low] : [low, ansi];
    }

    private static byte[] Truncate(byte[] bytes) =>
        bytes.Length <= MaxPasswordBytes ? bytes : bytes[..MaxPasswordBytes];

    private static byte Ror(byte value) => (byte)((value >> 1) | (value << 7));
}
