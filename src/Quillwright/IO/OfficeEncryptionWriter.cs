using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Quillwright.IO;

/// <summary>
/// Locks a package behind a password, in the scheme Office has used since 2010
/// ([MS-OFFCRYPTO] 2.3.4.10 to 2.3.4.15).
/// </summary>
/// <remarks>
/// <para>
/// The result is not a package at all: it is a compound file holding the whole package,
/// encrypted, in an <c>EncryptedPackage</c> stream, beside an <c>EncryptionInfo</c> stream
/// that describes in XML how to unlock it. Nothing above here knows the difference — the
/// saver writes an ordinary package and this wraps it.
/// </para>
/// <para>
/// Only the newer scheme is written. The older one hashes with SHA-1 and encrypts in one
/// block-independent pass, which is worth reading for the files that already exist and not
/// worth producing for the ones that do not.
/// </para>
/// </remarks>
internal static class OfficeEncryptionWriter
{
    private const int SaltBytes = 16;
    private const int BlockBytes = 16;
    private const int KeyBytes = 32;
    private const int SegmentBytes = 4096;
    private const int SpinCount = 100000;
    private const string HashName = "SHA512";

    private static ReadOnlySpan<byte> VerifierInputBlock => [0xFE, 0xA7, 0xD2, 0x76, 0x3B, 0x4B, 0x9E, 0x79];
    private static ReadOnlySpan<byte> VerifierValueBlock => [0xD7, 0xAA, 0x0F, 0x6D, 0x30, 0x61, 0x34, 0x4E];
    private static ReadOnlySpan<byte> SecretKeyBlock => [0x14, 0x6E, 0x0B, 0xE7, 0xAB, 0xAC, 0xD0, 0xD6];
    private static ReadOnlySpan<byte> IntegrityKeyBlock => [0x5F, 0xB2, 0xAD, 0x01, 0x0C, 0xB9, 0xE1, 0xF6];
    private static ReadOnlySpan<byte> IntegrityValueBlock => [0xA0, 0x67, 0x7F, 0x02, 0xB2, 0x2C, 0x84, 0x33];

    /// <summary>Locks a package with a password.</summary>
    /// <param name="package">The bytes of the ordinary package.</param>
    /// <param name="password">The password that will open it.</param>
    /// <returns>The compound file holding it.</returns>
    public static byte[] Encrypt(byte[] package, string password)
    {
        byte[] dataSalt = RandomNumberGenerator.GetBytes(SaltBytes);
        byte[] keySalt = RandomNumberGenerator.GetBytes(SaltBytes);
        byte[] secret = RandomNumberGenerator.GetBytes(KeyBytes);
        byte[] verifier = RandomNumberGenerator.GetBytes(SaltBytes);

        using HashAlgorithm hash = SHA512.Create();
        byte[] payload = Payload(hash, dataSalt, secret, package);
        byte[] iterated = CryptoPrimitives.IteratedHash(hash, keySalt, password, SpinCount);

        string description = Describe(
            dataSalt,
            keySalt,
            Lock(hash, iterated, keySalt, VerifierInputBlock, verifier),
            Lock(hash, iterated, keySalt, VerifierValueBlock, hash.ComputeHash(verifier)),
            Lock(hash, iterated, keySalt, SecretKeyBlock, secret),
            Integrity(hash, dataSalt, secret, payload));

        var container = new CompoundFileWriter();
        container.Add(OfficeCrypto.InfoStream, [.. Version(), .. Encoding.UTF8.GetBytes(description)]);
        container.Add(OfficeCrypto.PackageStream, payload);
        return container.Build();
    }

    /// <summary>The eight bytes before the description: the version, then the flags of 2.3.4.10.</summary>
    private static byte[] Version() => [0x04, 0x00, 0x04, 0x00, 0x40, 0x00, 0x00, 0x00];

    /// <summary>
    /// The package itself: its length, then its bytes in chained segments, each with an
    /// initialization vector of its own ([MS-OFFCRYPTO] 2.3.4.15).
    /// </summary>
    private static byte[] Payload(HashAlgorithm hash, byte[] dataSalt, byte[] secret, byte[] package)
    {
        var payload = new List<byte>(package.Length + 8 + SegmentBytes);
        payload.AddRange(BitConverter.GetBytes((long)package.Length));

        Span<byte> number = stackalloc byte[4];
        for (int segment = 0, at = 0; at < package.Length; segment++, at += SegmentBytes)
        {
            BinaryPrimitives.WriteInt32LittleEndian(number, segment);
            byte[] iv = CryptoPrimitives.Fit(hash.ComputeHash([.. dataSalt, .. number]), BlockBytes);
            int length = Math.Min(SegmentBytes, package.Length - at);
            payload.AddRange(CryptoPrimitives.EncryptAes(secret, iv, package.AsSpan(at, length)));
        }

        return [.. payload];
    }

    /// <summary>Encrypts one value with a key derived for its own purpose ([MS-OFFCRYPTO] 2.3.4.11).</summary>
    private static byte[] Lock(HashAlgorithm hash, byte[] iterated, byte[] salt, ReadOnlySpan<byte> block, byte[] value)
    {
        byte[] key = CryptoPrimitives.DeriveKey(hash, iterated, block, KeyBytes);
        return CryptoPrimitives.EncryptAes(key, CryptoPrimitives.Fit(salt, BlockBytes), value);
    }

    /// <summary>
    /// The check that says the encrypted package has not been tampered with
    /// ([MS-OFFCRYPTO] 2.3.4.14). Word verifies it, so a file written without it opens with a
    /// complaint even when the password is right.
    /// </summary>
    private static (byte[] Key, byte[] Value) Integrity(HashAlgorithm hash, byte[] dataSalt, byte[] secret, byte[] payload)
    {
        byte[] key = RandomNumberGenerator.GetBytes(hash.HashSize / 8);
        using var mac = new HMACSHA512(key);
        byte[] value = mac.ComputeHash(payload);

        return (
            CryptoPrimitives.EncryptAes(secret, Vector(hash, dataSalt, IntegrityKeyBlock), key),
            CryptoPrimitives.EncryptAes(secret, Vector(hash, dataSalt, IntegrityValueBlock), value));
    }

    private static byte[] Vector(HashAlgorithm hash, byte[] dataSalt, ReadOnlySpan<byte> block) =>
        CryptoPrimitives.Fit(hash.ComputeHash([.. dataSalt, .. block]), BlockBytes);

    /// <summary>The XML the scheme describes itself with ([MS-OFFCRYPTO] 2.3.4.10).</summary>
    private static string Describe(
        byte[] dataSalt,
        byte[] keySalt,
        byte[] verifierInput,
        byte[] verifierValue,
        byte[] encryptedKey,
        (byte[] Key, byte[] Value) integrity)
    {
        var builder = new StringBuilder(1024);
        builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n");
        builder.Append("<encryption xmlns=\"http://schemas.microsoft.com/office/2006/encryption\"");
        builder.Append(" xmlns:p=\"http://schemas.microsoft.com/office/2006/keyEncryptor/password\">");
        builder.Append("<keyData").Append(Common()).Append(" saltValue=\"").Append(Convert.ToBase64String(dataSalt)).Append("\"/>");
        builder.Append("<dataIntegrity encryptedHmacKey=\"").Append(Convert.ToBase64String(integrity.Key))
            .Append("\" encryptedHmacValue=\"").Append(Convert.ToBase64String(integrity.Value)).Append("\"/>");
        builder.Append("<keyEncryptors><keyEncryptor uri=\"http://schemas.microsoft.com/office/2006/keyEncryptor/password\">");
        builder.Append("<p:encryptedKey spinCount=\"").Append(SpinCount.ToString(CultureInfo.InvariantCulture)).Append('"')
            .Append(Common())
            .Append(" saltValue=\"").Append(Convert.ToBase64String(keySalt))
            .Append("\" encryptedVerifierHashInput=\"").Append(Convert.ToBase64String(verifierInput))
            .Append("\" encryptedVerifierHashValue=\"").Append(Convert.ToBase64String(verifierValue))
            .Append("\" encryptedKeyValue=\"").Append(Convert.ToBase64String(encryptedKey)).Append("\"/>");
        builder.Append("</keyEncryptor></keyEncryptors></encryption>");
        return builder.ToString();
    }

    /// <summary>The attributes both halves of the description share.</summary>
    private static string Common() =>
        $" saltSize=\"{SaltBytes}\" blockSize=\"{BlockBytes}\" keyBits=\"{KeyBytes * 8}\" hashSize=\"64\"" +
        $" cipherAlgorithm=\"AES\" cipherChaining=\"ChainingModeCBC\" hashAlgorithm=\"{HashName}\"";
}
