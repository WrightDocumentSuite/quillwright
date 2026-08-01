using System.Text;
using Quillwright.IO;
using Quillwright.Vba;

namespace Quillwright.Tests;

/// <summary>
/// Covers a project that really is locked, against a document Word wrote with a password on it.
/// </summary>
/// <remarks>
/// <para>
/// This is the fixture the protection code was missing. Word refuses to set a project password
/// through automation, so everything about a locked project had until now been exercised either
/// against the specification's worked values or against structures built here — which proves the
/// arithmetic but not that the arithmetic is aimed at the right bytes. The fixture was made by
/// hand, in Word, with the password <c>123</c>.
/// </para>
/// <para>
/// Both files are the same document saved twice in one session, so the two formats have to agree
/// about the lock as well as about the source.
/// </para>
/// </remarks>
public class VbaLockedProjectTests
{
    private const string Password = "123";

    public static TheoryData<string> Fixtures => ["macros-locked.docm", "macros-locked.doc"];

    /// <summary>
    /// The password is stored as a digest of itself with a random key, which cannot be read
    /// back — but a candidate put through the same steps has to come out matching, and anything
    /// else has to not.
    /// </summary>
    [Theory]
    [MemberData(nameof(Fixtures))]
    public void ThePasswordOnAProjectWordLocked_IsRecognised(string fixture)
    {
        Assert.SkipUnless(File.Exists(VbaFixtures.Path(fixture)), "The locked macro fixture is not present.");

        VbaProtection protection = Read(fixture).Protection;

        Assert.True(protection.HasPassword);
        Assert.Null(protection.Password);
        Assert.True(protection.IsPasswordCorrect(Password));
    }

    [Theory]
    [InlineData("124")]
    [InlineData("1234")]
    [InlineData("12")]
    [InlineData("")]
    [InlineData(" 123")]
    public void AnythingThatIsNotThePassword_IsRejected(string candidate)
    {
        Assert.SkipUnless(File.Exists(VbaFixtures.Path("macros-locked.docm")), "The locked macro fixture is not present.");

        Assert.False(Read("macros-locked.docm").Protection.IsPasswordCorrect(candidate));
    }

    /// <summary>
    /// Word locked this project through the editor and hid it, which is the pairing [MS-OVBA]
    /// 2.3.1.17 requires: a project with <c>GC</c> set to zero must also have
    /// <c>fVBEProtected</c>. Reading either of the two wrongly would break that agreement, so
    /// the two together check each other.
    /// </summary>
    [Theory]
    [MemberData(nameof(Fixtures))]
    public void AProjectLockedThroughTheEditor_IsAlsoHiddenByIt(string fixture)
    {
        Assert.SkipUnless(File.Exists(VbaFixtures.Path(fixture)), "The locked macro fixture is not present.");

        VbaProtection protection = Read(fixture).Protection;

        Assert.True(protection.IsProtected);
        Assert.True(protection.IsEditorProtected);
        Assert.False(protection.IsVisible);
        Assert.False(protection.IsUserProtected);
        Assert.False(protection.IsHostProtected);
    }

    /// <summary>
    /// The claim the whole of this reader rests on. A password guards the editor and nothing
    /// else: the modules of a locked project are listed, named and classified exactly as an
    /// unlocked one's are, because the lock never touched them.
    /// </summary>
    [Theory]
    [MemberData(nameof(Fixtures))]
    public void ALockedProject_ReadsLikeAnyOther(string fixture)
    {
        Assert.SkipUnless(File.Exists(VbaFixtures.Path(fixture)), "The locked macro fixture is not present.");

        VbaProject project = Read(fixture);

        Assert.Equal(["Class1", "ThisDocument"], project.Modules.Select(static m => m.Name).Order(StringComparer.Ordinal));
        Assert.Equal(VbaModuleKind.Class, Module(project, "Class1").Kind);
        Assert.Equal(VbaModuleKind.Document, Module(project, "ThisDocument").Kind);
        Assert.Contains("Attribute VB_Name = \"Class1\"", Module(project, "Class1").Code, StringComparison.Ordinal);
    }

    [Fact]
    public void BothFormatsOfTheLockedDocument_AgreeOnEverything()
    {
        Assert.SkipUnless(File.Exists(VbaFixtures.Path("macros-locked.doc")), "The locked macro fixture is not present.");
        Assert.SkipUnless(File.Exists(VbaFixtures.Path("macros-locked.docm")), "The locked macro fixture is not present.");

        VbaProject modern = Read("macros-locked.docm");
        VbaProject legacy = Read("macros-locked.doc");

        Assert.Equal(modern.ToSourceListing(), legacy.ToSourceListing());
        Assert.Equal(modern.References.Select(static r => r.Name), legacy.References.Select(static r => r.Name));
        Assert.Equal(modern.Protection.IsVisible, legacy.Protection.IsVisible);
        Assert.True(legacy.Protection.IsPasswordCorrect(Password));
    }

    /// <summary>
    /// [MS-OVBA] 2.4.4.1 — the hash Word wrote, checked field by field. This is the one thing
    /// no test built here could establish: that the reserved byte, the bit-field, the key, the
    /// digest and the terminator are where this code looks for them.
    /// </summary>
    [Fact]
    public void TheStoredHash_HasTheShapeTheSpecificationDescribes()
    {
        Assert.SkipUnless(File.Exists(VbaFixtures.Path("macros-locked.docm")), "The locked macro fixture is not present.");

        CompoundFile container = VbaFixtures.OpenProject("macros-locked.docm");
        VbaProjectStream stream = VbaProjectStream.Read(container.ReadStream("PROJECT"), Encoding.Latin1);
        byte[] hash = Assert.IsType<byte[]>(VbaEncryption.DecryptHex(stream.Password));

        Assert.Equal(29, hash.Length);
        Assert.Equal(0xFF, hash[0]);
        Assert.Equal(0x00, hash[^1]);
        Assert.DoesNotContain<byte>(0x00, hash[1..^1]);
        Assert.Equal<byte[]>([0x04, 0x00, 0x00, 0x00], VbaEncryption.DecryptHex(stream.ProtectionState));
        Assert.Equal<byte[]>([0x00], VbaEncryption.DecryptHex(stream.Visibility));
    }

    private static VbaProject Read(string fixture) =>
        fixture.EndsWith(".doc", StringComparison.Ordinal) ? VbaFixtures.ReadLegacy(fixture) : VbaFixtures.Read(fixture);

    private static VbaModule Module(VbaProject project, string name) =>
        Assert.Single(project.Modules, module => module.Name == name);
}
