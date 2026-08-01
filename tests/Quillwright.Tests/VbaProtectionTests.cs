using System.Security.Cryptography;
using System.Text;
using Quillwright.IO;
using Quillwright.Vba;

namespace Quillwright.Tests;

/// <summary>
/// Covers the obfuscation VBA puts over the protection values in the <c>PROJECT</c> stream.
/// </summary>
/// <remarks>
/// The three worked values in [MS-OVBA] 3.1.6 come with their decrypted contents printed beside
/// them, which makes them exact test vectors: a wrong cipher would produce noise rather than the
/// single sentinel byte the specification says to expect.
/// </remarks>
public class VbaProtectionTests
{
    /// <summary>Zeroes of its own, so that the bit-field over the key is not written off as all ones.</summary>
    private static readonly byte[] Key = [0x00, 0x41, 0x00, 0x42];

    /// <summary>[MS-OVBA] 3.1.6 — <c>CMG</c>, said to decrypt to four zero bytes.</summary>
    [Fact]
    public void TheProtectionStateExample_Decrypts()
    {
        byte[]? data = VbaEncryption.DecryptHex("0705D8E3D8EDDBF1DBF1DBF1DBF1");

        Assert.Equal<byte[]?>([0x00, 0x00, 0x00, 0x00], data);
    }

    /// <summary>[MS-OVBA] 3.1.6 — <c>DPB</c>, said to decrypt to a single zero: no password.</summary>
    [Fact]
    public void ThePasswordExample_Decrypts()
    {
        byte[]? data = VbaEncryption.DecryptHex("0E0CD1ECDFF4E7F5E7F5E7");

        Assert.Equal<byte[]?>([0x00], data);
    }

    /// <summary>[MS-OVBA] 3.1.6 — <c>GC</c>, said to decrypt to 0xFF: the project is visible.</summary>
    [Fact]
    public void TheVisibilityExample_Decrypts()
    {
        byte[]? data = VbaEncryption.DecryptHex("1517CAF1D6F9D7F9D706");

        Assert.Equal<byte[]?>([0xFF], data);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0E")]
    [InlineData("ZZZZZZZZZZZZZZZZ")]
    [InlineData("0000000000000000000000")]
    public void ValuesThatDoNotDecode_ComeBackAsNothing(string hex) =>
        Assert.Null(VbaEncryption.DecryptHex(hex));

    /// <summary>
    /// The reading has to hold for a file Word wrote, not only for the specification's example.
    /// An unprotected project records that as the single sentinel byte, so decoding the fixture
    /// to exactly that proves the cipher ran rather than quietly failing to nothing.
    /// </summary>
    [Fact]
    public void AnUnprotectedProjectWordWrote_DecryptsToTheSentinel()
    {
        Assert.SkipUnless(File.Exists(VbaFixtures.Path("macros.docm")), "The macro fixture is not present.");

        CompoundFile container = VbaFixtures.OpenProject("macros.docm");
        VbaProjectStream project = VbaProjectStream.Read(container.ReadStream("PROJECT"), Encoding.Latin1);

        Assert.NotNull(project.ProtectionState);
        Assert.NotNull(project.Password);
        Assert.Equal<byte[]?>([0x00, 0x00, 0x00, 0x00], VbaEncryption.DecryptHex(project.ProtectionState));
        Assert.Equal<byte[]?>([0x00], VbaEncryption.DecryptHex(project.Password));
    }

    [Fact]
    public void AnUnprotectedProject_ReportsNoProtection()
    {
        Assert.SkipUnless(File.Exists(VbaFixtures.Path("macros.docm")), "The macro fixture is not present.");

        VbaProtection protection = VbaFixtures.Read("macros.docm").Protection;

        Assert.False(protection.IsProtected);
        Assert.False(protection.HasPassword);
        Assert.False(protection.IsUserProtected);
        Assert.False(protection.IsEditorProtected);
        Assert.Null(protection.Password);
    }

    /// <summary>
    /// A project whose password is kept as text rather than as a hash gives the password up.
    /// The values are built here rather than taken from a file, because Word will not set a
    /// password through automation and the specification permits both forms.
    /// </summary>
    [Fact]
    public void APasswordStoredAsText_IsReadBack()
    {
        string encrypted = Obfuscate([.. "letmein"u8, 0x00]);

        VbaProtection protection = Protection("0705D8E3D8EDDBF1DBF1DBF1DBF1", encrypted);

        Assert.True(protection.HasPassword);
        Assert.True(protection.IsProtected);
        Assert.Equal("letmein", protection.Password);
    }

    /// <summary>A password kept as a hash is reported as present but cannot be read.</summary>
    [Fact]
    public void APasswordStoredAsAHash_IsReportedButNotRead()
    {
        VbaProtection protection = Protection(null, Obfuscate(HashStructure("letmein", Key)));

        Assert.True(protection.HasPassword);
        Assert.True(protection.IsProtected);
        Assert.Null(protection.Password);
    }

    [Fact]
    public void ProtectionFlags_AreReadOutOfTheStateByte()
    {
        Assert.True(Protection(Obfuscate([0x01, 0, 0, 0]), null).IsUserProtected);
        Assert.True(Protection(Obfuscate([0x02, 0, 0, 0]), null).IsHostProtected);
        Assert.True(Protection(Obfuscate([0x04, 0, 0, 0]), null).IsEditorProtected);
        Assert.False(Protection(Obfuscate([0x00, 0, 0, 0]), null).IsProtected);
    }

    /// <summary>
    /// [MS-OVBA] 2.3.1.17 — <c>GC</c> holds one byte, and a project that says nothing is
    /// visible. A project hidden this way is protected by the editor as well.
    /// </summary>
    [Fact]
    public void TheVisibilityState_IsReadAndDefaultsToVisible()
    {
        Assert.True(VbaProtection.Read(null, null, Obfuscate([0xFF]), Encoding.Latin1).IsVisible);
        Assert.False(VbaProtection.Read(null, null, Obfuscate([0x00]), Encoding.Latin1).IsVisible);
        Assert.True(VbaProtection.Read(null, null, null, Encoding.Latin1).IsVisible);
        Assert.True(VbaProtection.None.IsVisible);
    }

    [Fact]
    public void AProjectWordWrote_IsVisible()
    {
        Assert.SkipUnless(File.Exists(VbaFixtures.Path("macros.docm")), "The macro fixture is not present.");

        Assert.True(VbaFixtures.Read("macros.docm").Protection.IsVisible);
    }

    /// <summary>
    /// [MS-OVBA] 2.4.4 — the stored hash cannot be turned back into a password, but a candidate
    /// can be put through the same steps and the results compared.
    /// </summary>
    [Fact]
    public void APasswordCheckedAgainstTheStoredHash_IsAcceptedOnlyWhenItIsTheRightOne()
    {
        VbaProtection protection = Protection(null, Obfuscate(HashStructure("secret", Key)));

        Assert.True(protection.HasPassword);
        Assert.Null(protection.Password);
        Assert.True(protection.IsPasswordCorrect("secret"));
        Assert.False(protection.IsPasswordCorrect("Secret"));
        Assert.False(protection.IsPasswordCorrect("secret "));
        Assert.False(protection.IsPasswordCorrect(string.Empty));
    }

    /// <summary>
    /// The key and the digest travel through a null-terminated string, so their zero bytes are
    /// written as ones and recorded in a bit-field instead. This vector has zeroes in both, so
    /// reading the bit-field wrongly cannot pass.
    /// </summary>
    [Fact]
    public void TheHashVector_HasZeroBytesInBothOfTheEncodedFields()
    {
        byte[] structure = HashStructure("secret", Key);

        Assert.Contains<byte>(0x00, Key);
        Assert.Contains<byte>(0x00, SHA1.HashData([.. "secret"u8, .. Key]));
        Assert.Equal(29, structure.Length);
        Assert.Equal(0xFF, structure[0]);
        Assert.Equal(0x00, structure[^1]);
        Assert.DoesNotContain<byte>(0x00, structure[1..^1]);
    }

    /// <summary>A project with no password has nothing to check a password against.</summary>
    [Fact]
    public void AProjectWithNoPassword_AcceptsNoPassword()
    {
        VbaProtection protection = Protection(null, Obfuscate([0x00]));

        Assert.False(protection.HasPassword);
        Assert.False(protection.IsPasswordCorrect("secret"));
        Assert.False(protection.IsPasswordCorrect(string.Empty));
    }

    /// <summary>A password kept as text is checked against directly.</summary>
    [Fact]
    public void APasswordStoredAsText_IsCheckedAsWritten()
    {
        VbaProtection protection = Protection(null, Obfuscate([.. "letmein"u8, 0x00]));

        Assert.True(protection.IsPasswordCorrect("letmein"));
        Assert.False(protection.IsPasswordCorrect("LetMeIn"));
    }

    /// <summary>Twenty-nine bytes that are not a hash structure are not read as one.</summary>
    [Fact]
    public void SomethingThatIsNotAHash_IsNotTakenForOne()
    {
        byte[] structure = HashStructure("secret", Key);
        structure[0] = 0xFE;

        Assert.False(Protection(null, Obfuscate(structure)).IsPasswordCorrect("secret"));
    }

    /// <summary>
    /// Builds a Password Hash Data Structure ([MS-OVBA] 2.4.4.1) — a reserved byte, the bits
    /// saying which of the bytes that follow were zero, the key, the digest of the password
    /// with the key after it, and a terminator.
    /// </summary>
    /// <param name="password">The password to hash.</param>
    /// <param name="key">The four bytes hashed along with it.</param>
    private static byte[] HashStructure(string password, byte[] key)
    {
        byte[] digest = SHA1.HashData([.. Encoding.Latin1.GetBytes(password), .. key]);
        var structure = new List<byte> { 0xFF, 0x00, 0x00, 0x00 };

        int nulls = 0;
        int bit = 0;
        foreach (byte value in (byte[])[.. key, .. digest])
        {
            nulls |= (value == 0x00 ? 0 : 1) << bit++;
            structure.Add(value == 0x00 ? (byte)0x01 : value);
        }

        structure[1] = (byte)nulls;
        structure[2] = (byte)(nulls >> 8);
        structure[3] = (byte)(nulls >> 16);
        structure.Add(0x00);
        return [.. structure];
    }

    private static VbaProtection Protection(string? state, string? password) =>
        VbaProtection.Read(state, password, null, Encoding.Latin1);

    /// <summary>
    /// Applies the cipher of [MS-OVBA] 2.4.3.2 so that values the specification does not print
    /// can still be exercised. The encoder is checked by the three examples above, which the
    /// decoder passes independently of it.
    /// </summary>
    /// <param name="data">The bytes to obfuscate.</param>
    private static string Obfuscate(byte[] data)
    {
        const byte seed = 0x07;
        const byte version = 2;
        const byte projectKey = 0xDF;

        byte versionEnc = seed ^ version;
        byte projectKeyEnc = seed ^ projectKey;
        var output = new List<byte> { seed, versionEnc, projectKeyEnc };

        byte plain = projectKey;
        byte cipher = projectKeyEnc;
        byte previous = versionEnc;

        void Write(byte value)
        {
            byte encoded = (byte)(value ^ (byte)(previous + plain));
            output.Add(encoded);
            previous = cipher;
            cipher = encoded;
            plain = value;
        }

        for (int padding = (seed & 6) / 2; padding > 0; padding--)
            Write(seed);
        for (int i = 0; i < 4; i++)
            Write((byte)(data.Length >> (8 * i)));
        foreach (byte value in data)
            Write(value);

        return Convert.ToHexString([.. output]);
    }
}
