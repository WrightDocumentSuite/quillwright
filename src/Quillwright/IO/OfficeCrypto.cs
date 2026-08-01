using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Xml;
using Quillwright.Diagnostics;

namespace Quillwright.IO;

/// <summary>
/// Opens an encrypted OOXML document ([MS-OFFCRYPTO] 2.3.4).
/// </summary>
/// <remarks>
/// <para>
/// An encrypted document is not a package at all: it is a compound file holding the whole
/// package, encrypted, in an <c>EncryptedPackage</c> stream, beside an <c>EncryptionInfo</c>
/// stream describing how to unlock it. Decrypting turns it back into the zip every other part
/// of this library expects, so nothing above here knows the difference.
/// </para>
/// <para>
/// Two schemes are in use. The older one hashes the password fifty thousand times and
/// encrypts with AES in a single block-independent pass; the newer one describes itself in a
/// small XML document and encrypts in chained segments. Both are read; the newer one is also
/// what <see cref="OfficeEncryptionWriter"/> writes.
/// </para>
/// </remarks>
internal static class OfficeCrypto
{
    /// <summary>Name of the stream describing how the package is locked.</summary>
    public const string InfoStream = "EncryptionInfo";

    /// <summary>Name of the stream holding the package.</summary>
    public const string PackageStream = "EncryptedPackage";

    private static readonly byte[] VerifierInputBlock = [0xFE, 0xA7, 0xD2, 0x76, 0x3B, 0x4B, 0x9E, 0x79];
    private static readonly byte[] VerifierValueBlock = [0xD7, 0xAA, 0x0F, 0x6D, 0x30, 0x61, 0x34, 0x4E];
    private static readonly byte[] SecretKeyBlock = [0x14, 0x6E, 0x0B, 0xE7, 0xAB, 0xAC, 0xD0, 0xD6];
    private static readonly byte[] IntegrityKeyBlock = [0x5F, 0xB2, 0xAD, 0x01, 0x0C, 0xB9, 0xE1, 0xF6];
    private static readonly byte[] IntegrityValueBlock = [0xA0, 0x67, 0x7F, 0x02, 0xB2, 0x2C, 0x84, 0x33];
    private const int SegmentBytes = 4096;

    /// <summary>Whether a compound file holds an encrypted package rather than something else.</summary>
    /// <param name="container">The compound file.</param>
    public static bool IsEncryptedPackage(CompoundFile container)
    {
        HashSet<string> streams = [.. container.ChildrenOf(string.Empty)];
        return streams.Contains(InfoStream) && streams.Contains(PackageStream);
    }

    /// <summary>Decrypts the package a compound file holds.</summary>
    /// <param name="container">The compound file.</param>
    /// <param name="password">The password to open it with.</param>
    /// <exception cref="EncryptedDocumentException">No password, the wrong one, or a scheme this version does not read.</exception>
    public static byte[] DecryptPackage(CompoundFile container, string? password)
    {
        if (string.IsNullOrEmpty(password))
            throw new EncryptedDocumentException(
                "The document is encrypted. Supply the password through LoadOptions to open it.");

        byte[] info = container.ReadStream(InfoStream) ?? throw new EncryptedDocumentException(
            "The document says it is encrypted but does not say how.");
        byte[] payload = container.ReadStream(PackageStream) ?? throw new EncryptedDocumentException(
            "The document says it is encrypted but holds no encrypted package.");

        if (info.Length < 8)
            throw new EncryptedDocumentException("The document's encryption header is too short to read.");

        int major = BinaryPrimitives.ReadUInt16LittleEndian(info);
        int minor = BinaryPrimitives.ReadUInt16LittleEndian(info.AsSpan(2));
        return (major, minor) switch
        {
            (4, 4) => Agile(info, payload, password),
            (2 or 3 or 4, 2) => Standard(info, payload, password),
            _ => throw new EncryptedDocumentException(
                $"The document uses encryption version {major}.{minor}, which this version does not read."),
        };
    }

    /// <summary>
    /// The package as it was before encryption, given the whole encrypted stream and its
    /// unencrypted length ([MS-OFFCRYPTO] 2.3.4.4).
    /// </summary>
    private static byte[] Unwrap(byte[] plain, byte[] payload)
    {
        long size = BinaryPrimitives.ReadInt64LittleEndian(payload);
        if (size < 0 || size > plain.Length)
            throw new EncryptedDocumentException("The decrypted package is shorter than it says it is.");
        return plain.AsSpan(0, (int)size).ToArray();
    }

    /// <summary>Reads the older scheme, whose header is a fixed layout ([MS-OFFCRYPTO] 2.3.4.5).</summary>
    private static byte[] Standard(byte[] info, byte[] payload, string password)
    {
        int headerSize = BinaryPrimitives.ReadInt32LittleEndian(info.AsSpan(8));
        if (headerSize < 32 || 12 + headerSize + 40 > info.Length)
            throw new EncryptedDocumentException("The document's encryption header is malformed.");

        int keyBits = BinaryPrimitives.ReadInt32LittleEndian(info.AsSpan(12 + 16));
        int verifier = 12 + headerSize;
        int saltSize = BinaryPrimitives.ReadInt32LittleEndian(info.AsSpan(verifier));
        if (saltSize != 16)
            throw new EncryptedDocumentException("The document's encryption salt is not the size the format requires.");

        byte[] salt = info.AsSpan(verifier + 4, 16).ToArray();
        using HashAlgorithm hash = SHA1.Create();
        byte[] key = StandardKey(hash, salt, password, Math.Max(keyBits, 128) / 8);

        // Verifying the password first turns "the file is corrupt" into "the password is wrong".
        byte[] check = CryptoPrimitives.DecryptAes(key, null, info.AsSpan(verifier + 20, 16));
        byte[] stored = CryptoPrimitives.DecryptAes(key, null, info.AsSpan(verifier + 40, 32));
        if (!hash.ComputeHash(check).AsSpan().SequenceEqual(stored.AsSpan(0, 20)))
            throw new EncryptedDocumentException("The password does not open this document.");

        return Unwrap(CryptoPrimitives.DecryptAes(key, null, payload.AsSpan(8)), payload);
    }

    /// <summary>The key of the older scheme, whose derivation is its own ([MS-OFFCRYPTO] 2.3.4.7).</summary>
    private static byte[] StandardKey(HashAlgorithm hash, byte[] salt, string password, int length)
    {
        byte[] iterated = CryptoPrimitives.IteratedHash(hash, salt, password, spinCount: 50000);
        byte[] final = hash.ComputeHash([.. iterated, 0, 0, 0, 0]);

        // Two hashes of the final value, padded out to a block and inverted against each
        // other, concatenated and cut to length.
        byte[] x1 = hash.ComputeHash(Pad(final, 0x36));
        byte[] x2 = hash.ComputeHash(Pad(final, 0x5C));
        return CryptoPrimitives.Fit([.. x1, .. x2], length);
    }

    private static byte[] Pad(byte[] value, byte filler)
    {
        var buffer = new byte[64];
        buffer.AsSpan().Fill(filler);
        for (int i = 0; i < value.Length && i < buffer.Length; i++)
            buffer[i] ^= value[i];
        return buffer;
    }

    /// <summary>Reads the newer scheme, which describes itself in XML ([MS-OFFCRYPTO] 2.3.4.10).</summary>
    private static byte[] Agile(byte[] info, byte[] payload, string password)
    {
        AgileDescriptor descriptor = AgileDescriptor.Parse(info);
        AgileKeyData encryptor = descriptor.Encryptor;
        AgileKeyData data = descriptor.Data;
        CipherMode encryptorMode = Cipher(encryptor, "password key encryptor");
        CipherMode dataMode = Cipher(data, "package data");

        using HashAlgorithm hash = CryptoPrimitives.Hash(encryptor.HashAlgorithm)
            ?? throw new EncryptedDocumentException(
                $"The document is hashed with {encryptor.HashAlgorithm}, which this version does not read.");
        using HashAlgorithm dataHash = CryptoPrimitives.Hash(data.HashAlgorithm)
            ?? throw new EncryptedDocumentException(
                $"The document is hashed with {data.HashAlgorithm}, which this version does not read.");
        if (encryptor.HashSize != hash.HashSize / 8 || data.HashSize != dataHash.HashSize / 8 ||
            !encryptor.CipherAlgorithm.Equals(data.CipherAlgorithm, StringComparison.OrdinalIgnoreCase))
        {
            throw new EncryptedDocumentException("The document's agile hash or cipher parameters disagree.");
        }

        byte[] iterated = CryptoPrimitives.IteratedHash(hash, encryptor.Salt, password, encryptor.SpinCount);
        byte[] input = Decrypt(
            encryptor, encryptorMode, hash, iterated, VerifierInputBlock, descriptor.VerifierHashInput);
        byte[] expected = Decrypt(
            encryptor, encryptorMode, hash, iterated, VerifierValueBlock, descriptor.VerifierHashValue);
        int hashBytes = hash.HashSize / 8;
        if (expected.Length < hashBytes ||
            !CryptographicOperations.FixedTimeEquals(hash.ComputeHash(input), expected.AsSpan(0, hashBytes)))
        {
            throw new EncryptedDocumentException("The password does not open this document.");
        }

        byte[] unlocked = Decrypt(
            encryptor, encryptorMode, hash, iterated, SecretKeyBlock, descriptor.EncryptedKey);
        int keyBytes = data.KeyBits / 8;
        if (unlocked.Length < keyBytes)
            throw new EncryptedDocumentException("The document's encrypted package key is too short.");

        byte[] secret = unlocked[..keyBytes];
        VerifyIntegrity(descriptor, dataHash, dataMode, secret, payload);
        return Unwrap(DecryptSegments(data, dataHash, dataMode, secret, payload), payload);
    }

    private static byte[] Decrypt(
        AgileKeyData encryptor, CipherMode mode, HashAlgorithm hash, byte[] iterated,
        byte[] block, byte[] data)
    {
        byte[] key = CryptoPrimitives.DeriveKey(hash, iterated, block, encryptor.KeyBits / 8);
        return CryptoPrimitives.DecryptAes(
            key, CryptoPrimitives.Fit(encryptor.Salt, encryptor.BlockSize), data, mode);
    }

    private static void VerifyIntegrity(
        AgileDescriptor descriptor, HashAlgorithm hash, CipherMode mode, byte[] secret, byte[] payload)
    {
        if (descriptor.EncryptedHmacKey.Length == 0 && descriptor.EncryptedHmacValue.Length == 0)
            return;
        if (descriptor.EncryptedHmacKey.Length == 0 || descriptor.EncryptedHmacValue.Length == 0)
            throw new EncryptedDocumentException("The document's agile integrity description is incomplete.");

        AgileKeyData data = descriptor.Data;
        byte[] key = CryptoPrimitives.DecryptAes(
            secret, Vector(hash, data, IntegrityKeyBlock), descriptor.EncryptedHmacKey, mode);
        byte[] expected = CryptoPrimitives.DecryptAes(
            secret, Vector(hash, data, IntegrityValueBlock), descriptor.EncryptedHmacValue, mode);

        int hashBytes = data.HashSize;
        if (hashBytes <= 0 || key.Length < hashBytes || expected.Length < hashBytes)
            throw new EncryptedDocumentException("The document's agile integrity values are too short.");

        byte[] actual = Hmac(data.HashAlgorithm, key[..hashBytes], payload);
        if (actual.Length < hashBytes ||
            !CryptographicOperations.FixedTimeEquals(actual.AsSpan(0, hashBytes), expected.AsSpan(0, hashBytes)))
        {
            throw new EncryptedDocumentException("The encrypted package failed its integrity check.");
        }
    }

    private static byte[] Vector(HashAlgorithm hash, AgileKeyData data, byte[] block) =>
        CryptoPrimitives.Fit(hash.ComputeHash([.. data.Salt, .. block]), data.BlockSize);

    private static byte[] Hmac(string name, byte[] key, byte[] payload)
    {
        HashAlgorithmName algorithm = name.ToUpperInvariant() switch
        {
            "SHA1" or "SHA-1" => HashAlgorithmName.SHA1,
            "SHA256" or "SHA-256" => HashAlgorithmName.SHA256,
            "SHA384" or "SHA-384" => HashAlgorithmName.SHA384,
            "SHA512" or "SHA-512" => HashAlgorithmName.SHA512,
            _ => default,
        };

        if (algorithm == default)
            throw new EncryptedDocumentException(
                $"The document is hashed with {name}, which this version does not read.");

        using IncrementalHash hmac = IncrementalHash.CreateHMAC(algorithm, key);
        hmac.AppendData(payload);
        return hmac.GetHashAndReset();
    }

    private static CipherMode Cipher(AgileKeyData data, string role)
    {
        if (!data.CipherAlgorithm.Equals("AES", StringComparison.OrdinalIgnoreCase))
        {
            throw new EncryptedDocumentException(
                $"The document encrypts its {role} with {data.CipherAlgorithm}, which this version does not read.");
        }

        CipherMode mode = data.CipherChaining switch
        {
            "ChainingModeCBC" => CipherMode.CBC,
            "ChainingModeCFB" => CipherMode.CFB,
            _ => default,
        };
        if (mode == default)
        {
            throw new EncryptedDocumentException(
                $"The document uses {data.CipherChaining} for its {role}, which this version does not read.");
        }

        if (data.BlockSize != 16 || data.KeyBits is not (128 or 192 or 256) ||
            data.SaltSize <= 0 || data.Salt.Length != data.SaltSize ||
            data.SpinCount is < 0 or > 10000000)
        {
            throw new EncryptedDocumentException($"The document's agile {role} parameters are malformed.");
        }

        return mode;
    }

    /// <summary>
    /// Decrypts the package in the segments the newer scheme chains it in, each with an
    /// initialization vector of its own ([MS-OFFCRYPTO] 2.3.4.15).
    /// </summary>
    private static byte[] DecryptSegments(
        AgileKeyData data, HashAlgorithm hash, CipherMode mode, byte[] secret, byte[] payload)
    {
        if (payload.Length < 8)
            throw new EncryptedDocumentException("The encrypted package stream is too short.");

        var plain = new byte[Math.Max(0, payload.Length - 8)];
        Span<byte> number = stackalloc byte[4];
        for (int segment = 0, at = 8; at < payload.Length; segment++, at += SegmentBytes)
        {
            BinaryPrimitives.WriteInt32LittleEndian(number, segment);
            byte[] iv = CryptoPrimitives.Fit(hash.ComputeHash([.. data.Salt, .. number]), data.BlockSize);
            int length = Math.Min(SegmentBytes, payload.Length - at);
            CryptoPrimitives.DecryptAes(secret, iv, payload.AsSpan(at, length), mode).CopyTo(plain.AsSpan(at - 8));
        }

        return plain;
    }
}

/// <summary>One <c>keyData</c> or <c>encryptedKey</c> element of the newer scheme.</summary>
/// <param name="Salt">The salt its keys are derived from.</param>
/// <param name="SaltSize">The declared number of bytes in <paramref name="Salt"/>.</param>
/// <param name="BlockSize">Bytes in one cipher block.</param>
/// <param name="KeyBits">Bits in its key.</param>
/// <param name="HashSize">Bytes in the hash output.</param>
/// <param name="SpinCount">How many times the password hash is iterated.</param>
/// <param name="HashAlgorithm">Name of the hashing algorithm.</param>
/// <param name="CipherAlgorithm">Name of the block cipher.</param>
/// <param name="CipherChaining">Name of its chaining mode.</param>
internal readonly record struct AgileKeyData(
    byte[] Salt,
    int SaltSize,
    int BlockSize,
    int KeyBits,
    int HashSize,
    int SpinCount,
    string HashAlgorithm,
    string CipherAlgorithm,
    string CipherChaining);

/// <summary>The XML the newer scheme describes itself with ([MS-OFFCRYPTO] 2.3.4.10).</summary>
internal sealed class AgileDescriptor
{
    private AgileDescriptor(
        AgileKeyData data, AgileKeyData encryptor, byte[] input, byte[] value, byte[] key,
        byte[] hmacKey, byte[] hmacValue)
    {
        Data = data;
        Encryptor = encryptor;
        VerifierHashInput = input;
        VerifierHashValue = value;
        EncryptedKey = key;
        EncryptedHmacKey = hmacKey;
        EncryptedHmacValue = hmacValue;
    }

    /// <summary>How the package itself is encrypted.</summary>
    public AgileKeyData Data { get; }

    /// <summary>How the key to it is locked away behind the password.</summary>
    public AgileKeyData Encryptor { get; }

    /// <summary>The random bytes the password is checked against.</summary>
    public byte[] VerifierHashInput { get; }

    /// <summary>Their hash, as the file stores it.</summary>
    public byte[] VerifierHashValue { get; }

    /// <summary>The package's own key, locked with the password.</summary>
    public byte[] EncryptedKey { get; }

    /// <summary>The integrity HMAC key, encrypted with the package key.</summary>
    public byte[] EncryptedHmacKey { get; }

    /// <summary>The integrity HMAC value, encrypted with the package key.</summary>
    public byte[] EncryptedHmacValue { get; }

    /// <summary>Reads the descriptor out of the stream that begins with the version and a reserved word.</summary>
    /// <param name="info">The whole <c>EncryptionInfo</c> stream.</param>
    public static AgileDescriptor Parse(byte[] info)
    {
        AgileKeyData data = default;
        AgileKeyData encryptor = default;
        byte[] input = [];
        byte[] value = [];
        byte[] key = [];
        byte[] hmacKey = [];
        byte[] hmacValue = [];
        bool passwordEncryptor = false;

        const string EncryptionNamespace = "http://schemas.microsoft.com/office/2006/encryption";
        const string PasswordNamespace = "http://schemas.microsoft.com/office/2006/keyEncryptor/password";

        using var xml = XmlReader.Create(new MemoryStream(info, 8, info.Length - 8), Xml.XmlDefaults.ReaderSettings);
        while (xml.Read())
        {
            if (xml.NodeType != XmlNodeType.Element)
                continue;

            switch (xml.LocalName, xml.NamespaceURI)
            {
                case ("keyData", EncryptionNamespace):
                    data = ReadKeyData(xml);
                    break;
                case ("dataIntegrity", EncryptionNamespace):
                    hmacKey = Bytes(xml, "encryptedHmacKey");
                    hmacValue = Bytes(xml, "encryptedHmacValue");
                    break;
                case ("encryptedKey", PasswordNamespace):
                    if (passwordEncryptor)
                        throw new EncryptedDocumentException(
                            "The document has more than one agile password key encryptor.");

                    passwordEncryptor = true;
                    encryptor = ReadKeyData(xml);
                    input = Bytes(xml, "encryptedVerifierHashInput");
                    value = Bytes(xml, "encryptedVerifierHashValue");
                    key = Bytes(xml, "encryptedKeyValue");
                    break;
            }
        }

        if (data.Salt is null || encryptor.Salt is null || input.Length == 0 || value.Length == 0 || key.Length == 0)
            throw new EncryptedDocumentException("The document's encryption description is incomplete.");

        return new AgileDescriptor(data, encryptor, input, value, key, hmacKey, hmacValue);
    }

    private static AgileKeyData ReadKeyData(XmlReader xml) => new(
        Bytes(xml, "saltValue"),
        Number(xml, "saltSize", 0),
        Number(xml, "blockSize", 16),
        Number(xml, "keyBits", 128),
        Number(xml, "hashSize", 0),
        Number(xml, "spinCount", 0),
        xml.GetAttribute("hashAlgorithm") ?? string.Empty,
        xml.GetAttribute("cipherAlgorithm") ?? string.Empty,
        xml.GetAttribute("cipherChaining") ?? string.Empty);

    private static byte[] Bytes(XmlReader xml, string name) =>
        xml.GetAttribute(name) is { Length: > 0 } encoded ? Convert.FromBase64String(encoded) : [];

    private static int Number(XmlReader xml, string name, int fallback) =>
        int.TryParse(xml.GetAttribute(name), System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : fallback;
}
