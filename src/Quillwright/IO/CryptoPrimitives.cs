using System.Security.Cryptography;
using System.Text;

namespace Quillwright.IO;

/// <summary>
/// The hashing and block-cipher pieces the Office encryption schemes are built out of
/// ([MS-OFFCRYPTO] 2.3.4 and 2.3.5).
/// </summary>
/// <remarks>
/// Both schemes derive a key by hashing a salt with the password and then iterating, and both
/// decrypt with AES. What differs is which hash, how many iterations, and whether the cipher
/// chains — so those are parameters here rather than three near-copies of the same code.
/// </remarks>
internal static class CryptoPrimitives
{
    /// <summary>The password as the specification requires it: Unicode characters, little-endian.</summary>
    public static byte[] Encode(string password) => Encoding.Unicode.GetBytes(password);

    /// <summary>Creates the hash a scheme names, or <see langword="null"/> when it names one we do not have.</summary>
    /// <param name="name">The algorithm's name as the file spells it.</param>
    public static HashAlgorithm? Hash(string name) => name.ToUpperInvariant() switch
    {
        "SHA1" or "SHA-1" => SHA1.Create(),
        "SHA256" or "SHA-256" => SHA256.Create(),
        "SHA384" or "SHA-384" => SHA384.Create(),
        "SHA512" or "SHA-512" => SHA512.Create(),
        _ => null,
    };

    /// <summary>
    /// The iterated password hash both schemes start from: the salt and the password hashed
    /// together, then the result hashed with a counter as many times as the file asks for
    /// ([MS-OFFCRYPTO] 2.3.4.7 and 2.3.4.11).
    /// </summary>
    /// <param name="hash">The hashing algorithm.</param>
    /// <param name="salt">The salt the file stores.</param>
    /// <param name="password">The password to try.</param>
    /// <param name="spinCount">How many times to iterate; zero for the schemes that do not.</param>
    public static byte[] IteratedHash(HashAlgorithm hash, byte[] salt, string password, int spinCount)
    {
        byte[] digest = hash.ComputeHash([.. salt, .. Encode(password)]);
        Span<byte> counter = stackalloc byte[4];
        for (int i = 0; i < spinCount; i++)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(counter, i);
            digest = hash.ComputeHash([.. counter, .. digest]);
        }

        return digest;
    }

    /// <summary>Hashes a value together with a block key, then fits it to the length a key needs.</summary>
    /// <param name="hash">The hashing algorithm.</param>
    /// <param name="value">The iterated password hash.</param>
    /// <param name="blockKey">The bytes that keep two blocks from sharing a key.</param>
    /// <param name="length">Length the result must have.</param>
    public static byte[] DeriveKey(HashAlgorithm hash, byte[] value, ReadOnlySpan<byte> blockKey, int length) =>
        Fit(hash.ComputeHash([.. value, .. blockKey]), length);

    /// <summary>
    /// Pads a value with <c>0x36</c> or truncates it, which is how the schemes make a hash fit
    /// a key or an initialization vector ([MS-OFFCRYPTO] 2.3.4.11 and 2.3.4.12).
    /// </summary>
    /// <param name="value">The value to fit.</param>
    /// <param name="length">Length the result must have.</param>
    public static byte[] Fit(byte[] value, int length)
    {
        if (value.Length == length)
            return value;

        var fitted = new byte[length];
        if (value.Length > length)
        {
            value.AsSpan(0, length).CopyTo(fitted);
            return fitted;
        }

        value.CopyTo(fitted, 0);
        fitted.AsSpan(value.Length).Fill(0x36);
        return fitted;
    }

    /// <summary>Decrypts with AES, in whichever mode the scheme uses and with no padding of its own.</summary>
    /// <param name="key">The derived key.</param>
    /// <param name="iv">The initialization vector, or <see langword="null"/> for the mode that has none.</param>
    /// <param name="data">The bytes to decrypt; the length is rounded down to whole blocks.</param>
    public static byte[] DecryptAes(byte[] key, byte[]? iv, ReadOnlySpan<byte> data) =>
        Transform(key, iv, data, iv is null ? CipherMode.ECB : CipherMode.CBC, encrypt: false);

    /// <summary>Decrypts AES data in the chaining mode named by an agile descriptor.</summary>
    /// <param name="key">The derived key.</param>
    /// <param name="iv">The initialization vector.</param>
    /// <param name="data">The bytes to decrypt.</param>
    /// <param name="mode">CBC, or CFB with the eight-bit feedback window required by MS-OFFCRYPTO.</param>
    public static byte[] DecryptAes(byte[] key, byte[] iv, ReadOnlySpan<byte> data, CipherMode mode) =>
        Transform(key, iv, data, mode, encrypt: false);

    /// <summary>
    /// Encrypts with AES, the same way round. The schemes pad their own plaintext out to a
    /// whole number of blocks, so the cipher adds none of its own.
    /// </summary>
    /// <param name="key">The derived key.</param>
    /// <param name="iv">The initialization vector, or <see langword="null"/> for the mode that has none.</param>
    /// <param name="data">The bytes to encrypt; the length is rounded up to whole blocks with zeroes.</param>
    public static byte[] EncryptAes(byte[] key, byte[]? iv, ReadOnlySpan<byte> data) =>
        Transform(key, iv, Blocks(data, 16), iv is null ? CipherMode.ECB : CipherMode.CBC, encrypt: true);

    /// <summary>Rounds a value up to a whole number of cipher blocks, padding with zeroes.</summary>
    /// <param name="data">The bytes.</param>
    /// <param name="blockSize">Bytes in one block.</param>
    public static byte[] Blocks(ReadOnlySpan<byte> data, int blockSize)
    {
        int whole = ((data.Length + blockSize - 1) / blockSize) * blockSize;
        var padded = new byte[whole];
        data.CopyTo(padded);
        return padded;
    }

    private static byte[] Transform(
        byte[] key, byte[]? iv, ReadOnlySpan<byte> data, CipherMode mode, bool encrypt)
    {
        using Aes aes = Aes.Create();
        aes.Key = key;
        aes.Mode = mode;
        aes.Padding = PaddingMode.None;
        if (mode == CipherMode.CFB)
            aes.FeedbackSize = 8;
        if (iv is not null)
            aes.IV = iv;

        using ICryptoTransform transform = encrypt ? aes.CreateEncryptor() : aes.CreateDecryptor();
        int whole = data.Length - (data.Length % transform.InputBlockSize);
        return whole == 0 ? [] : transform.TransformFinalBlock(data[..whole].ToArray(), 0, whole);
    }
}
