using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Quillwright.Diagnostics;
using Quillwright.IO;

namespace Quillwright.Doc;

/// <summary>
/// Unlocks a password-protected legacy document ([MS-DOC] 2.2.6, [MS-OFFCRYPTO] 2.3.5 and
/// 2.3.6).
/// </summary>
/// <remarks>
/// <para>
/// Three streams are encrypted — the document, the table and the data — each in blocks of
/// 512 bytes with a key of its own derived from the block number. The header describing how
/// sits unencrypted at the front of the table stream, and the first 68 bytes of the document
/// stream stay readable so that a reader can find the header at all; the keystream runs over
/// them regardless, so the bytes that follow line up.
/// </para>
/// <para>
/// Reading is all this does. A document opened with a password is written back unencrypted,
/// because this library does not encrypt.
/// </para>
/// </remarks>
internal static class DocDecryptor
{
    private const int BlockBytes = 512;

    /// <summary>Bytes of the document stream that stay readable however the file is locked.</summary>
    private const int PlainPrefix = 68;

    /// <summary>Whether the header says the file is locked, and how.</summary>
    /// <param name="document">The <c>WordDocument</c> stream.</param>
    public static (bool Encrypted, bool Obfuscated) Protection(byte[] document)
    {
        if (document.Length < 16)
            return (false, false);

        ushort flags = BinaryPrimitives.ReadUInt16LittleEndian(document.AsSpan(10));
        return ((flags & 0x0100) != 0, (flags & 0x8000) != 0);
    }

    /// <summary>Decrypts the three streams a locked document keeps its content in.</summary>
    /// <param name="document">The <c>WordDocument</c> stream.</param>
    /// <param name="table">The table stream.</param>
    /// <param name="data">The data stream, which may be empty.</param>
    /// <param name="password">The password to open it with.</param>
    /// <exception cref="EncryptedDocumentException">No password, the wrong one, or a scheme this version does not read.</exception>
    public static (byte[] Document, byte[] Table, byte[] Data) Decrypt(
        byte[] document, byte[] table, byte[] data, string? password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        // Obfuscation keeps a verifier where the other schemes keep the size of a header, so
        // which of the two the number means is settled before it is read as either.
        if (Protection(document).Obfuscated)
        {
            uint verifier = BinaryPrimitives.ReadUInt32LittleEndian(document.AsSpan(14));
            if (!XorObfuscation.Verify(password, verifier))
                throw new EncryptedDocumentException("The password does not open this document.");

            return XorObfuscation.Decode(document, table, data, password, verifier);
        }

        int headerBytes = BinaryPrimitives.ReadInt32LittleEndian(document.AsSpan(14));
        if (headerBytes < 8 || headerBytes > table.Length)
            throw new EncryptedDocumentException("The document's encryption header is malformed.");

        DocCipher cipher = DocCipher.Read(table, headerBytes, password);
        return (Transform(cipher, document, PlainPrefix), Transform(cipher, table, headerBytes), Transform(cipher, data, 0));
    }

    /// <summary>
    /// Decrypts a stream in blocks, leaving the first bytes alone but still running the
    /// keystream over them so that everything after stays aligned.
    /// </summary>
    private static byte[] Transform(DocCipher cipher, byte[] stream, int plain)
    {
        var result = new byte[stream.Length];
        stream.CopyTo(result, 0);

        for (int at = 0, block = 0; at < stream.Length; at += BlockBytes, block++)
        {
            int length = Math.Min(BlockBytes, stream.Length - at);
            Span<byte> window = result.AsSpan(at, length);
            Rc4.Apply(cipher.KeyFor(block), window);

            // The readable prefix is put back after the fact rather than skipped, because the
            // cipher has to advance over it either way.
            if (at < plain)
                stream.AsSpan(at, Math.Min(plain - at, length)).CopyTo(window);
        }

        return result;
    }
}

/// <summary>The key derivation of one locked document, which differs between the two schemes.</summary>
internal sealed class DocCipher
{
    private readonly byte[] _seed;
    private readonly int _keyBytes;
    private readonly bool _cryptoApi;

    private DocCipher(byte[] seed, int keyBytes, bool cryptoApi)
    {
        _seed = seed;
        _keyBytes = keyBytes;
        _cryptoApi = cryptoApi;
    }

    /// <summary>Reads the header at the front of the table stream and checks the password against it.</summary>
    /// <param name="table">The table stream.</param>
    /// <param name="headerBytes">How many of its bytes the header occupies.</param>
    /// <param name="password">The password to try.</param>
    public static DocCipher Read(byte[] table, int headerBytes, string password)
    {
        int major = BinaryPrimitives.ReadUInt16LittleEndian(table);
        DocCipher cipher = major == 1
            ? Legacy(table, password)
            : CryptoApi(table, headerBytes, password);

        return cipher;
    }

    /// <summary>The key for one 512-byte block.</summary>
    /// <param name="block">Zero-based block number.</param>
    public byte[] KeyFor(int block)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(number, block);

        if (!_cryptoApi)
            return MD5.HashData([.. _seed, .. number])[.._keyBytes];

        byte[] hash = SHA1.HashData([.. _seed, .. number]);

        // A forty-bit key is padded out to a hundred and twenty-eight with zeroes rather than
        // used as it stands ([MS-OFFCRYPTO] 2.3.5.2).
        if (_keyBytes != 5)
            return hash[.._keyBytes];

        var padded = new byte[16];
        hash.AsSpan(0, 5).CopyTo(padded);
        return padded;
    }

    /// <summary>The newer of the two binary schemes ([MS-OFFCRYPTO] 2.3.5).</summary>
    private static DocCipher CryptoApi(byte[] table, int headerBytes, string password)
    {
        int headerSize = BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(8));
        if (headerSize < 24 || 12 + headerSize + 40 > table.Length)
            throw new EncryptedDocumentException("The document's encryption header is malformed.");

        int keyBits = BinaryPrimitives.ReadInt32LittleEndian(table.AsSpan(12 + 16));
        int verifier = 12 + headerSize;
        byte[] salt = table.AsSpan(verifier + 4, 16).ToArray();
        var cipher = new DocCipher(
            SHA1.HashData([.. salt, .. Encoding.Unicode.GetBytes(password)]),
            Math.Clamp(keyBits == 0 ? 40 : keyBits, 40, 128) / 8,
            cryptoApi: true);

        cipher.Verify(
            table.AsSpan(verifier + 20, 16).ToArray(),
            table.AsSpan(verifier + 36, 20).ToArray(),
            static value => SHA1.HashData(value));
        _ = headerBytes;
        return cipher;
    }

    /// <summary>The older of the two, whose derivation folds the salt in sixteen times ([MS-OFFCRYPTO] 2.3.6.2).</summary>
    private static DocCipher Legacy(byte[] table, string password)
    {
        if (table.Length < 52)
            throw new EncryptedDocumentException("The document's encryption header is malformed.");

        byte[] salt = table.AsSpan(4, 16).ToArray();
        byte[] truncated = MD5.HashData(Encoding.Unicode.GetBytes(password))[..5];

        var buffer = new byte[336];
        for (int i = 0; i < 16; i++)
        {
            truncated.CopyTo(buffer, i * 21);
            salt.CopyTo(buffer, (i * 21) + 5);
        }

        var cipher = new DocCipher(MD5.HashData(buffer)[..5], keyBytes: 16, cryptoApi: false);
        cipher.Verify(
            table.AsSpan(20, 16).ToArray(),
            table.AsSpan(36, 16).ToArray(),
            static value => MD5.HashData(value));
        return cipher;
    }

    /// <summary>
    /// Checks the password against the verifier the file stores, so that a wrong one is
    /// reported as wrong rather than as a corrupt document.
    /// </summary>
    private void Verify(byte[] encryptedVerifier, byte[] encryptedHash, Func<byte[], byte[]> hash)
    {
        byte[] key = KeyFor(0);
        byte[] verifier = (byte[])encryptedVerifier.Clone();
        byte[] stored = (byte[])encryptedHash.Clone();

        // Both fields are one continuous stretch of the same keystream.
        var both = new byte[verifier.Length + stored.Length];
        verifier.CopyTo(both, 0);
        stored.CopyTo(both, verifier.Length);
        Rc4.Apply(key, both);

        byte[] expected = hash(both[..verifier.Length]);
        if (!expected.AsSpan(0, stored.Length).SequenceEqual(both.AsSpan(verifier.Length)))
            throw new EncryptedDocumentException("The password does not open this document.");
    }
}
