using System.Buffers.Binary;
using Quillwright.IO;

namespace Quillwright.Doc.Tests;

/// <summary>
/// Scrambles a legacy document the way the oldest scheme does ([MS-OFFCRYPTO] 2.3.7, applied
/// as [MS-DOC] 2.2.6.1).
/// </summary>
/// <remarks>
/// The library reads this scheme and deliberately never writes it, so a fixture has to exist
/// somewhere. It is written here from the specification rather than through the reader's own
/// helpers, so that a mistake shared by both would not cancel itself out — the same bargain
/// <see cref="OfficeEncryptor"/> makes for the schemes that came after.
/// </remarks>
internal static class XorObfuscator
{
    private const int PlainPrefix = 68;

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

    /// <summary>Scrambles the three content streams of a legacy document.</summary>
    /// <param name="file">The whole compound file, unlocked.</param>
    /// <param name="password">The password that will open it.</param>
    public static byte[] Obfuscate(byte[] file, string password)
    {
        CompoundFile source = CompoundFile.Open(file);
        byte[] document = source.ReadStream("WordDocument")!;
        byte[] table = source.ReadStream("1Table") ?? source.ReadStream("0Table")!;
        byte[]? data = source.ReadStream("Data");

        byte[] narrowed = Narrow(password);
        uint verifier = ((uint)Key(narrowed) << 16) | ShortVerifier(narrowed);

        // The header says the file is scrambled before it is, because it is one of the bytes
        // that stays readable and a reader looks at it first.
        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(document.AsSpan(10));
        BinaryPrimitives.WriteUInt16LittleEndian(document.AsSpan(10), (ushort)(flags | 0x0100 | 0x8000));
        BinaryPrimitives.WriteUInt32LittleEndian(document.AsSpan(14), verifier);

        byte[] pad = Array(narrowed);
        var target = new CompoundFileWriter();
        target.Add("WordDocument", Transform(document, pad, PlainPrefix));
        target.Add(source.ReadStream("1Table") is not null ? "1Table" : "0Table", Transform(table, pad, 0));
        if (data is not null)
            target.Add("Data", Transform(data, pad, 0));

        return target.Build();
    }

    private static byte[] Transform(byte[] stream, byte[] pad, int plain)
    {
        var result = (byte[])stream.Clone();
        for (int at = plain; at < result.Length; at++)
        {
            if (result[at] == 0)
                continue;

            byte transformed = (byte)(result[at] ^ pad[at % pad.Length]);
            if (transformed != 0)
                result[at] = transformed;
        }

        return result;
    }

    private static byte[] Array(byte[] password)
    {
        ushort key = Key(password);
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

    private static ushort Key(byte[] password)
    {
        ushort key = InitialCode[password.Length - 1];
        int element = 0x68;
        for (int i = password.Length - 1; i >= 0; i--)
        {
            int character = password[i];
            for (int bit = 0; bit < 7; bit++, element--)
            {
                if ((character & 0x40) != 0)
                    key ^= Matrix[element];
                character <<= 1;
            }
        }

        return key;
    }

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

    private static byte[] Narrow(string password)
    {
        var bytes = new byte[Math.Min(password.Length, 15)];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)password[i] != 0 ? (byte)password[i] : (byte)(password[i] >> 8);
        return bytes;
    }

    private static byte Ror(byte value) => (byte)((value >> 1) | (value << 7));
}
