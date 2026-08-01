using System.Security.Cryptography;

namespace Quillwright.Vba;

/// <summary>
/// The hash a project keeps in place of its password ([MS-OVBA] 2.4.4).
/// </summary>
/// <remarks>
/// <para>
/// The structure is a four-byte key and a twenty-byte digest of the password with that key
/// appended. It cannot be turned back into a password, but a candidate can be put through the
/// same steps and the results compared, which is what the specification asks of a reader.
/// </para>
/// <para>
/// Neither field is stored plainly: every zero byte is written as one instead, and a bit-field
/// ahead of them records which bytes that happened to. The reason is that the whole structure
/// travels through the <c>PROJECT</c> stream as a null-terminated string.
/// </para>
/// </remarks>
internal sealed class VbaPasswordHash
{
    /// <summary>Size of the structure, which is how a hash is told from a stored password.</summary>
    public const int StructureLength = 29;

    private const byte Reserved = 0xFF;
    private const int KeyLength = 4;
    private const int DigestLength = 20;

    private readonly byte[] _key;
    private readonly byte[] _digest;

    private VbaPasswordHash(byte[] key, byte[] digest)
    {
        _key = key;
        _digest = digest;
    }

    /// <summary>Reads the structure out of a decrypted <c>DPB</c> value.</summary>
    /// <param name="data">The decrypted bytes.</param>
    /// <returns>The hash, or <see langword="null"/> when the bytes are not one.</returns>
    public static VbaPasswordHash? Read(ReadOnlySpan<byte> data)
    {
        if (data.Length != StructureLength || data[0] != Reserved || data[^1] != 0x00)
            return null;

        // Twenty-four bits of them: four for the key, then twenty for the digest.
        int nulls = data[1] | (data[2] << 8) | (data[3] << 16);
        return new VbaPasswordHash(
            Restore(data.Slice(4, KeyLength), nulls),
            Restore(data.Slice(4 + KeyLength, DigestLength), nulls >> KeyLength));
    }

    /// <summary>Whether a password is the one the hash was made from ([MS-OVBA] 2.4.4.5).</summary>
    /// <param name="password">The candidate, in the project's code page.</param>
    public bool Matches(ReadOnlySpan<byte> password)
    {
        byte[] material = new byte[password.Length + KeyLength];
        password.CopyTo(material);
        _key.CopyTo(material.AsSpan(password.Length));

        Span<byte> digest = stackalloc byte[DigestLength];
        SHA1.HashData(material, digest);
        return CryptographicOperations.FixedTimeEquals(digest, _digest);
    }

    /// <summary>Puts back the zero bytes the bit-field says were taken out ([MS-OVBA] 2.4.4.3).</summary>
    /// <param name="encoded">The bytes as stored.</param>
    /// <param name="flags">Bits for those bytes, the lowest standing for the first.</param>
    private static byte[] Restore(ReadOnlySpan<byte> encoded, int flags)
    {
        byte[] decoded = new byte[encoded.Length];
        for (int i = 0; i < encoded.Length; i++)
            decoded[i] = (flags >> i & 1) == 0 ? (byte)0x00 : encoded[i];

        return decoded;
    }
}
