using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Quillwright.IO;

namespace Quillwright.Doc.Writing;

/// <summary>
/// Locks a legacy document behind a password ([MS-OFFCRYPTO] 2.3.5, applied as [MS-DOC]
/// 2.2.6.3).
/// </summary>
/// <remarks>
/// <para>
/// The three content streams are encrypted with RC4 in blocks of 512 bytes, each block keyed
/// by its own number so that a reader can jump into the middle of one. The header describing
/// how sits unencrypted at the front of the table stream, and the first sixty-eight bytes of
/// the document stream stay readable so that a reader can find that header at all — the
/// keystream runs over them regardless, so everything after lines up.
/// </para>
/// <para>
/// This scheme is what Word writes for a <c>.doc</c> and it is not strong. It is offered
/// because it is the only thing the format has: a caller who wants real encryption should be
/// writing <c>.docx</c>, where <see cref="Quillwright.IO.OfficeEncryptionWriter"/> writes AES.
/// </para>
/// </remarks>
internal static class DocEncryptor
{
    private const int BlockBytes = 512;

    /// <summary>Bytes of the document stream that stay readable however the file is locked.</summary>
    private const int PlainPrefix = 68;

    /// <summary>Bytes before the header body: the version, the flags and the body's own size.</summary>
    private const int PrefixBytes = 12;

    /// <summary>Bytes of the header body before the provider's name ([MS-OFFCRYPTO] 2.3.2).</summary>
    private const int BodyBytes = 32;

    /// <summary>Bits of the key, which for the provider named here is a hundred and twenty-eight.</summary>
    private const int KeyBits = 128;

    /// <summary>Bytes of the verifier that follows the header: the salt, and the check itself.</summary>
    private const int VerifierBytes = 4 + 16 + 16 + 20;

    private const string ProviderName = "Microsoft Enhanced RSA and AES Cryptographic Provider\0";

    /// <summary>
    /// How many bytes of the table stream the header occupies. It depends on nothing but the
    /// provider's name, so the space can be reserved before anything else is written — which
    /// it has to be, because every offset the header block records is measured from the front
    /// of the table stream.
    /// </summary>
    public static int HeaderLength =>
        PrefixBytes + BodyBytes + Encoding.Unicode.GetByteCount(ProviderName) + VerifierBytes;

    /// <summary>Locks the three streams.</summary>
    /// <param name="document">The <c>WordDocument</c> stream, header and all.</param>
    /// <param name="table">The table stream, with <see cref="HeaderLength"/> bytes reserved at the front.</param>
    /// <param name="data">The data stream, which may be empty.</param>
    /// <param name="password">The password that will open it.</param>
    public static (byte[] Document, byte[] Table, byte[] Data) Encrypt(
        byte[] document, byte[] table, byte[] data, string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] seed = SHA1.HashData([.. salt, .. Encoding.Unicode.GetBytes(password)]);
        Header(salt, seed).CopyTo(table.AsSpan());

        return (
            Transform(seed, document, PlainPrefix),
            Transform(seed, table, HeaderLength),
            Transform(seed, data, 0));
    }

    /// <summary>
    /// Encrypts a stream in blocks, leaving the first bytes alone but still running the
    /// keystream over them so that everything after stays aligned. RC4 is its own inverse, so
    /// this is the same walk the reader makes.
    /// </summary>
    private static byte[] Transform(byte[] seed, byte[] stream, int plain)
    {
        var result = new byte[stream.Length];
        stream.CopyTo(result, 0);

        for (int at = 0, block = 0; at < stream.Length; at += BlockBytes, block++)
        {
            int length = Math.Min(BlockBytes, stream.Length - at);
            Span<byte> window = result.AsSpan(at, length);
            Rc4.Apply(KeyFor(seed, block), window);

            if (at < plain)
                stream.AsSpan(at, Math.Min(plain - at, length)).CopyTo(window);
        }

        return result;
    }

    /// <summary>The key for one block, which is the seed hashed together with the block number.</summary>
    private static byte[] KeyFor(byte[] seed, int block)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(number, block);
        return SHA1.HashData([.. seed, .. number])[..(KeyBits / 8)];
    }

    /// <summary>
    /// The header the reader finds at the front of the table stream: how the file is locked,
    /// and the verifier that says whether a password is the right one ([MS-OFFCRYPTO] 2.3.5.1
    /// and 2.3.3).
    /// </summary>
    private static byte[] Header(byte[] salt, byte[] seed)
    {
        // The provider name is part of the header and is what fixes the key length; it is
        // stored as a null-terminated Unicode string.
        byte[] provider = Encoding.Unicode.GetBytes(ProviderName);
        var header = new byte[PrefixBytes + BodyBytes + provider.Length];
        Span<byte> span = header;

        BinaryPrimitives.WriteUInt16LittleEndian(span, 3);          // Major version.
        BinaryPrimitives.WriteUInt16LittleEndian(span[2..], 2);     // Minor version: CryptoAPI.
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], 0x14);  // fCryptoAPI and fDocProps clear.
        BinaryPrimitives.WriteInt32LittleEndian(span[8..], BodyBytes + provider.Length);

        Span<byte> body = span[PrefixBytes..];
        BinaryPrimitives.WriteUInt32LittleEndian(body, 0x14);            // The same flags again.
        BinaryPrimitives.WriteUInt32LittleEndian(body[4..], 0);          // No extra data.
        BinaryPrimitives.WriteUInt32LittleEndian(body[8..], 0x6801);     // RC4.
        BinaryPrimitives.WriteUInt32LittleEndian(body[12..], 0x8004);    // SHA-1.
        BinaryPrimitives.WriteInt32LittleEndian(body[16..], KeyBits);
        BinaryPrimitives.WriteUInt32LittleEndian(body[20..], 1);         // Provider type: any.
        provider.CopyTo(body[BodyBytes..]);

        return [.. header, .. Verifier(salt, seed)];
    }

    /// <summary>
    /// The salt and the encrypted verifier a reader checks a password against
    /// ([MS-OFFCRYPTO] 2.3.3). Both halves are one continuous stretch of keystream.
    /// </summary>
    private static byte[] Verifier(byte[] salt, byte[] seed)
    {
        byte[] verifier = RandomNumberGenerator.GetBytes(16);
        byte[] both = [.. verifier, .. SHA1.HashData(verifier)];
        Rc4.Apply(KeyFor(seed, 0), both);

        var bytes = new byte[4 + salt.Length + both.Length];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, salt.Length);
        salt.CopyTo(bytes, 4);
        both.CopyTo(bytes, 4 + salt.Length);
        return bytes;
    }
}
