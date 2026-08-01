using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Quillwright.Doc.Writing;
using Quillwright.IO;

namespace Quillwright.Doc.Tests;

/// <summary>
/// Locks a package the way Office does, so that the reader's unlocking can be tested against
/// something other than itself.
/// </summary>
/// <remarks>
/// This exists only here and works from [MS-OFFCRYPTO] 2.3.4 directly rather than through the
/// reader or writer's helpers: a mistake shared by production's two halves would otherwise
/// cancel itself out and prove nothing.
/// </remarks>
internal static class OfficeEncryptor
{
    private static readonly byte[] VerifierInputBlock = [0xFE, 0xA7, 0xD2, 0x76, 0x3B, 0x4B, 0x9E, 0x79];
    private static readonly byte[] VerifierValueBlock = [0xD7, 0xAA, 0x0F, 0x6D, 0x30, 0x61, 0x34, 0x4E];
    private static readonly byte[] SecretKeyBlock = [0x14, 0x6E, 0x0B, 0xE7, 0xAB, 0xAC, 0xD0, 0xD6];
    private static readonly byte[] IntegrityKeyBlock = [0x5F, 0xB2, 0xAD, 0x01, 0x0C, 0xB9, 0xE1, 0xF6];
    private static readonly byte[] IntegrityValueBlock = [0xA0, 0x67, 0x7F, 0x02, 0xB2, 0x2C, 0x84, 0x33];
    private const int SegmentBytes = 4096;
    private const int SpinCount = 10000;

    /// <summary>Locks a package with the older scheme ([MS-OFFCRYPTO] 2.3.4.5).</summary>
    /// <param name="package">The package to lock.</param>
    /// <param name="password">The password that will open it.</param>
    public static byte[] Standard(byte[] package, string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] key = StandardKey(salt, password);
        byte[] verifier = RandomNumberGenerator.GetBytes(16);
        byte[] hash = new byte[32];
        SHA1.HashData(verifier).CopyTo(hash, 0);

        var info = new List<byte>();
        info.AddRange(Int32(0x00020002));
        info.AddRange(Int32(0x24));
        info.AddRange(Int32(StandardHeader().Length));
        info.AddRange(StandardHeader());
        info.AddRange(Int32(salt.Length));
        info.AddRange(salt);
        info.AddRange(Encrypt(key, null, verifier));
        info.AddRange(Int32(20));
        info.AddRange(Encrypt(key, null, hash));

        byte[] payload = [.. Int64(package.Length), .. Encrypt(key, null, Block(package, 16))];
        return Container([.. info], payload);
    }

    /// <summary>Locks a package with the newer scheme ([MS-OFFCRYPTO] 2.3.4.10).</summary>
    /// <param name="package">The package to lock.</param>
    /// <param name="password">The password that will open it.</param>
    public static byte[] Agile(
        byte[] package, string password, bool certificateEncryptor = false, bool cfb = false,
        bool dataIntegrity = false)
    {
        byte[] dataSalt = RandomNumberGenerator.GetBytes(16);
        byte[] keySalt = RandomNumberGenerator.GetBytes(16);
        byte[] secret = RandomNumberGenerator.GetBytes(32);
        byte[] input = RandomNumberGenerator.GetBytes(16);

        byte[] iterated = AgileIterated(keySalt, password);
        byte[] encryptedInput = Encrypt(AgileKey(iterated, VerifierInputBlock), keySalt, Block(input, 16), cfb);
        byte[] encryptedValue = Encrypt(
            AgileKey(iterated, VerifierValueBlock), keySalt, Block(SHA512.HashData(input), 16), cfb);
        byte[] encryptedKey = Encrypt(AgileKey(iterated, SecretKeyBlock), keySalt, Block(secret, 16), cfb);

        var payload = new List<byte>(Int64(package.Length));
        byte[] padded = Block(package, 16);
        for (int segment = 0, at = 0; at < padded.Length; segment++, at += SegmentBytes)
        {
            byte[] iv = Fit(SHA512.HashData([.. dataSalt, .. Int32(segment)]), 16);
            payload.AddRange(Encrypt(
                secret, iv, padded.AsSpan(at, Math.Min(SegmentBytes, padded.Length - at)).ToArray(), cfb));
        }

        (byte[] Key, byte[] Value)? integrity = null;
        if (dataIntegrity)
        {
            byte[] hmacKey = RandomNumberGenerator.GetBytes(64);
            byte[] payloadBytes = [.. payload];
            byte[] hmacValue = HMACSHA512.HashData(hmacKey, payloadBytes);
            integrity = (
                Encrypt(secret, IntegrityVector(dataSalt, IntegrityKeyBlock), hmacKey, cfb),
                Encrypt(secret, IntegrityVector(dataSalt, IntegrityValueBlock), hmacValue, cfb));
        }

        byte[] info = [.. Int32(0x00040004), .. Int32(0x40), .. Encoding.UTF8.GetBytes(Descriptor(
            dataSalt, keySalt, encryptedInput, encryptedValue, encryptedKey,
            certificateEncryptor, cfb, integrity))];
        return Container(info, [.. payload]);
    }

    private static string Descriptor(
        byte[] dataSalt, byte[] keySalt, byte[] input, byte[] value, byte[] key,
        bool certificateEncryptor, bool cfb, (byte[] Key, byte[] Value)? integrity)
    {
        string chaining = cfb ? "ChainingModeCFB" : "ChainingModeCBC";
        string certificate = certificateEncryptor
            ? "<keyEncryptor uri=\"http://schemas.microsoft.com/office/2006/keyEncryptor/certificate\">" +
              "<c:encryptedKey xmlns:c=\"http://schemas.microsoft.com/office/2006/keyEncryptor/certificate\" " +
              "encryptedKeyValue=\"AA==\" X509Certificate=\"AA==\" certVerifier=\"AA==\"/>" +
              "</keyEncryptor>"
            : string.Empty;
        string integrityMarkup = integrity is { } check
            ? $"<dataIntegrity encryptedHmacKey=\"{Convert.ToBase64String(check.Key)}\" " +
              $"encryptedHmacValue=\"{Convert.ToBase64String(check.Value)}\"/>"
            : string.Empty;

        return
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<encryption xmlns=\"http://schemas.microsoft.com/office/2006/encryption\"" +
        " xmlns:p=\"http://schemas.microsoft.com/office/2006/keyEncryptor/password\">" +
        $"<keyData saltSize=\"16\" blockSize=\"16\" keyBits=\"256\" hashSize=\"64\" cipherAlgorithm=\"AES\"" +
        $" cipherChaining=\"{chaining}\" hashAlgorithm=\"SHA512\" saltValue=\"{Convert.ToBase64String(dataSalt)}\"/>" +
        integrityMarkup +
        "<keyEncryptors><keyEncryptor uri=\"http://schemas.microsoft.com/office/2006/keyEncryptor/password\">" +
        $"<p:encryptedKey spinCount=\"{SpinCount.ToString(CultureInfo.InvariantCulture)}\" saltSize=\"16\" blockSize=\"16\"" +
        $" keyBits=\"256\" hashSize=\"64\" cipherAlgorithm=\"AES\" cipherChaining=\"{chaining}\" hashAlgorithm=\"SHA512\"" +
        $" saltValue=\"{Convert.ToBase64String(keySalt)}\"" +
        $" encryptedVerifierHashInput=\"{Convert.ToBase64String(input)}\"" +
        $" encryptedVerifierHashValue=\"{Convert.ToBase64String(value)}\"" +
        $" encryptedKeyValue=\"{Convert.ToBase64String(key)}\"/>" +
        "</keyEncryptor>" + certificate + "</keyEncryptors></encryption>";
    }

    private static byte[] IntegrityVector(byte[] salt, byte[] block) =>
        Fit(SHA512.HashData([.. salt, .. block]), 16);

    /// <summary>Rewrites the agile XML descriptor without changing the encrypted payload.</summary>
    public static byte[] RewriteDescriptor(byte[] locked, Func<string, string> edit) =>
        Rewrite(locked, info =>
        {
            string descriptor = Encoding.UTF8.GetString(info, 8, info.Length - 8);
            return [.. info.AsSpan(0, 8), .. Encoding.UTF8.GetBytes(edit(descriptor))];
        }, static payload => payload);

    /// <summary>Rewrites the encrypted package stream without changing its descriptor.</summary>
    public static byte[] RewritePayload(byte[] locked, Func<byte[], byte[]> edit) =>
        Rewrite(locked, static info => info, edit);

    private static byte[] Rewrite(
        byte[] locked, Func<byte[], byte[]> editInfo, Func<byte[], byte[]> editPayload)
    {
        CompoundFile source = CompoundFile.Open(locked);
        return Container(
            editInfo(source.ReadStream("EncryptionInfo")!),
            editPayload(source.ReadStream("EncryptedPackage")!));
    }

    /// <summary>The older scheme's key: fifty thousand hashes, then two padded halves ([MS-OFFCRYPTO] 2.3.4.7).</summary>
    private static byte[] StandardKey(byte[] salt, string password)
    {
        byte[] digest = SHA1.HashData([.. salt, .. Encoding.Unicode.GetBytes(password)]);
        for (int i = 0; i < 50000; i++)
            digest = SHA1.HashData([.. Int32(i), .. digest]);

        byte[] final = SHA1.HashData([.. digest, .. Int32(0)]);
        return Fit([.. SHA1.HashData(Xor(final, 0x36)), .. SHA1.HashData(Xor(final, 0x5C))], 16);
    }

    private static byte[] AgileIterated(byte[] salt, string password)
    {
        byte[] digest = SHA512.HashData([.. salt, .. Encoding.Unicode.GetBytes(password)]);
        for (int i = 0; i < SpinCount; i++)
            digest = SHA512.HashData([.. Int32(i), .. digest]);
        return digest;
    }

    private static byte[] AgileKey(byte[] iterated, byte[] block) => Fit(SHA512.HashData([.. iterated, .. block]), 32);

    private static byte[] Xor(byte[] value, byte filler)
    {
        var buffer = new byte[64];
        buffer.AsSpan().Fill(filler);
        for (int i = 0; i < value.Length; i++)
            buffer[i] ^= value[i];
        return buffer;
    }

    private static byte[] Fit(byte[] value, int length)
    {
        var fitted = new byte[length];
        value.AsSpan(0, Math.Min(value.Length, length)).CopyTo(fitted);
        if (value.Length < length)
            fitted.AsSpan(value.Length).Fill(0x36);
        return fitted;
    }

    private static byte[] Block(byte[] value, int size)
    {
        int padded = ((value.Length + size - 1) / size) * size;
        var buffer = new byte[padded];
        value.CopyTo(buffer, 0);
        return buffer;
    }

    private static byte[] Encrypt(byte[] key, byte[]? iv, byte[] data, bool cfb = false)
    {
        using Aes aes = Aes.Create();
        aes.Key = key;
        aes.Mode = iv is null ? CipherMode.ECB : cfb ? CipherMode.CFB : CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        if (cfb)
            aes.FeedbackSize = 8;
        if (iv is not null)
            aes.IV = iv;

        using ICryptoTransform transform = aes.CreateEncryptor();
        return transform.TransformFinalBlock(data, 0, data.Length);
    }

    /// <summary>The fixed header of the older scheme, naming AES-128 and SHA-1.</summary>
    private static byte[] StandardHeader() =>
    [
        .. Int32(0x24), .. Int32(0), .. Int32(0x660E), .. Int32(0x8004), .. Int32(0x80), .. Int32(0x18),
        .. Int32(0), .. Int32(0), .. Encoding.Unicode.GetBytes("Microsoft Enhanced RSA and AES Cryptographic Provider\0"),
    ];

    private static byte[] Container(byte[] info, byte[] payload)
    {
        var writer = new CompoundFileWriter();
        writer.Add("EncryptionInfo", info);
        writer.Add("EncryptedPackage", payload);
        return writer.Build();
    }

    private static byte[] Int32(int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        return bytes;
    }

    private static byte[] Int64(long value)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
        return bytes;
    }
}
