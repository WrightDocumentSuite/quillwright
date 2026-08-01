namespace Quillwright.Vba;

/// <summary>
/// Reverses the obfuscation VBA applies to a few values in the <c>PROJECT</c> stream
/// ([MS-OVBA] 2.4.3), among them the protection state and the password.
/// </summary>
/// <remarks>
/// <para>
/// The scheme is a byte cipher, not encryption in any useful sense: each byte is exclusive-ored
/// with the sum of the previous cipher byte but one and the previous plain byte, so the whole
/// thing unwinds from a seed stored in the clear at the front. A couple of bytes of padding,
/// whose count also comes from the seed, sit between the header and the length.
/// </para>
/// <para>
/// It exists to stop casual reading of a file in a text editor, and it is described in the
/// published specification, so reversing it reveals nothing that was meant to be secret.
/// </para>
/// </remarks>
internal static class VbaEncryption
{
    private const byte ExpectedVersion = 2;
    private const int HeaderLength = 3;
    private const int LengthFieldBytes = 4;

    /// <summary>Unwinds an encrypted data structure ([MS-OVBA] 2.4.3.1).</summary>
    /// <param name="encrypted">The bytes the hexadecimal string decoded to.</param>
    /// <returns>The data it holds, or <see langword="null"/> when the bytes do not decode.</returns>
    public static byte[]? Decrypt(ReadOnlySpan<byte> encrypted)
    {
        if (encrypted.Length < HeaderLength + LengthFieldBytes)
            return null;

        byte seed = encrypted[0];
        byte versionEnc = encrypted[1];
        if ((byte)(seed ^ versionEnc) != ExpectedVersion)
            return null;

        byte projectKeyEnc = encrypted[2];
        var state = new CipherState((byte)(seed ^ projectKeyEnc), projectKeyEnc, versionEnc);

        int at = HeaderLength;
        for (int padding = (seed & 6) / 2; padding > 0; padding--)
        {
            if (at >= encrypted.Length)
                return null;
            state.Next(encrypted[at++]);
        }

        long length = 0;
        for (int i = 0; i < LengthFieldBytes; i++)
        {
            if (at >= encrypted.Length)
                return null;
            length |= (long)state.Next(encrypted[at++]) << (8 * i);
        }

        if (length < 0 || length > encrypted.Length - at)
            return null;

        var data = new byte[length];
        for (int i = 0; i < data.Length; i++)
            data[i] = state.Next(encrypted[at++]);

        return data;
    }

    /// <summary>Decodes a hexadecimal string from the <c>PROJECT</c> stream and unwinds it.</summary>
    /// <param name="hex">The quoted value, without its quotation marks.</param>
    public static byte[]? DecryptHex(string? hex)
    {
        if (hex is null || hex.Length < 2 || hex.Length % 2 != 0)
            return null;

        try
        {
            return Decrypt(Convert.FromHexString(hex));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>The three bytes of history the cipher carries from one byte to the next.</summary>
    /// <param name="plain">The last plain byte.</param>
    /// <param name="cipher">The last cipher byte.</param>
    /// <param name="previousCipher">The cipher byte before that.</param>
    private struct CipherState(byte plain, byte cipher, byte previousCipher)
    {
        /// <summary>Decodes one byte and advances the history.</summary>
        /// <param name="encrypted">The next cipher byte.</param>
        public byte Next(byte encrypted)
        {
            byte decoded = (byte)(encrypted ^ (byte)(previousCipher + plain));
            previousCipher = cipher;
            cipher = encrypted;
            plain = decoded;
            return decoded;
        }
    }
}
