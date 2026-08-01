using System.Buffers.Binary;
using System.Text;
using Quillwright.Vba;

namespace Quillwright.Tests;

/// <summary>
/// Covers the framing of the <c>dir</c> stream on records that the fixtures do not happen to
/// contain.
/// </summary>
/// <remarks>
/// Word writes a narrow subset of what [MS-OVBA] allows, so a fixture cannot reach the records
/// that other producers write — a control reference wrapped in the library it was generated
/// from, or a reference that leaves out the name it shares with the one before it. These are
/// built by hand from the record layouts in 2.3.4.2 instead, which is the only way to exercise
/// them at all.
/// </remarks>
public class VbaDirectoryTests
{
    private const string Original = @"*\G{0D452EE1-E08F-101A-852E-02608C4D0BB4}#2.0#0#C:\Windows\system32\FM20.DLL#Microsoft Forms 2.0 Object Library";
    private const string Extended = @"*\G{896C2D83-5466-46ED-8FAE-4C3E4F85E710}#2.0#0#C:\Temp\VBE\MSForms.exd#Microsoft Forms 2.0 Object Library";
    private const string Placeholder = @"*\G{00000000-0000-0000-0000-000000000000}#0.0#0##";

    /// <summary>
    /// [MS-OVBA] 2.3.4.2.2.4 — a control reference can be wrapped in a record naming the
    /// registered library its own was generated from. That is the useful one of the two: the
    /// identifier inside the control record points at a cache file in a temporary directory.
    /// </summary>
    [Fact]
    public void AControlReferenceWrappedInAnOriginal_KeepsBothIdentifiers()
    {
        byte[] dir = new Records()
            .CodePage(1252)
            .Name("MSForms")
            .Record(0x0033, Encoding.Latin1.GetBytes(Original))
            .Control(Placeholder, Extended, "MSForms")
            .EndOfModules()
            .Build();

        VbaReference reference = Assert.Single(VbaDirectory.Read(dir).References);

        Assert.Equal("MSForms", reference.Name);
        Assert.Equal(VbaReferenceKind.Control, reference.Kind);
        Assert.Equal(Extended, reference.Libid);
        Assert.Equal(Original, reference.OriginalLibid);
    }

    /// <summary>
    /// The name inside a control reference belongs to the extended library, and the name record
    /// of a reference is optional — so carrying the one over into the other would put a name on
    /// a reference that never had one.
    /// </summary>
    [Fact]
    public void AReferenceWithNoNameOfItsOwn_DoesNotBorrowTheOneBeforeIt()
    {
        byte[] dir = new Records()
            .CodePage(1252)
            .Name("MSForms")
            .Control(Placeholder, Extended, "MSForms")
            .Registered("*\\G{00020430-0000-0000-C000-000000000046}#2.0#0#stdole2.tlb#OLE Automation")
            .EndOfModules()
            .Build();

        VbaDirectory directory = VbaDirectory.Read(dir);

        Assert.Equal(2, directory.References.Count);
        Assert.Equal("MSForms", directory.References[0].Name);
        Assert.Equal(string.Empty, directory.References[1].Name);
        Assert.Equal(VbaReferenceKind.Registered, directory.References[1].Kind);
    }

    /// <summary>A reference that names no original says so rather than repeating itself.</summary>
    [Fact]
    public void AReferenceWithNoOriginal_ReportsNone()
    {
        byte[] dir = new Records()
            .CodePage(1252)
            .Name("stdole")
            .Registered("*\\G{00020430-0000-0000-C000-000000000046}#2.0#0#stdole2.tlb#OLE Automation")
            .EndOfModules()
            .Build();

        Assert.Null(Assert.Single(VbaDirectory.Read(dir).References).OriginalLibid);
    }

    /// <summary>
    /// [MS-OVBA] 2.3.4.2.3.2.4 — a description is written twice, and the UTF-16 copy is the one
    /// to prefer, but a producer that leaves it empty still meant what the other copy says.
    /// </summary>
    [Fact]
    public void AModuleDescribedOnlyInTheCodePage_KeepsThatDescription()
    {
        byte[] dir = new Records()
            .CodePage(1252)
            .Module("Greeting", "Says hello", unicodeDescription: null)
            .Build();

        VbaModuleRecord module = Assert.Single(VbaDirectory.Read(dir).Modules);

        Assert.Null(module.UnicodeDescription);
        Assert.Equal("Says hello", Encoding.Latin1.GetString(module.Description));
    }

    [Fact]
    public void AModuleDescribedTwice_PrefersTheUnicodeCopy()
    {
        byte[] dir = new Records()
            .CodePage(1252)
            .Module("Greeting", "Says hello", "Says hello")
            .Build();

        Assert.Equal("Says hello", Assert.Single(VbaDirectory.Read(dir).Modules).UnicodeDescription);
    }

    /// <summary>
    /// Records are framed by their own lengths, so a reference read wrongly does not cost one
    /// reference — it costs everything after it, the module list included.
    /// </summary>
    [Fact]
    public void ModulesAfterAWrappedControlReference_AreStillFound()
    {
        byte[] dir = new Records()
            .CodePage(1252)
            .Name("MSForms")
            .Record(0x0033, Encoding.Latin1.GetBytes(Original))
            .Control(Placeholder, Extended, "MSForms")
            .EndOfModules()
            .Module("Launcher", null, null)
            .Module("Scripted", null, null)
            .Build();

        Assert.Equal(
            ["Launcher", "Scripted"],
            VbaDirectory.Read(dir).Modules.Select(static m => Encoding.Latin1.GetString(m.Name)));
    }

    /// <summary>
    /// Builds a <c>dir</c> stream out of the record layouts of [MS-OVBA] 2.3.4.2. Every record
    /// is an identifier, a length and that many bytes; the fields the specification calls
    /// reserved sit where a length would and are written as one.
    /// </summary>
    private sealed class Records
    {
        private readonly List<byte> _bytes = [];

        public byte[] Build() => [.. _bytes];

        public Records Record(int id, params byte[] data)
        {
            _bytes.AddRange([(byte)id, (byte)(id >> 8)]);
            _bytes.AddRange(Number(data.Length));
            _bytes.AddRange(data);
            return this;
        }

        public Records CodePage(int codePage) => Record(0x0003, (byte)codePage, (byte)(codePage >> 8));

        /// <summary>A REFERENCENAME record, which is the name in both of its spellings.</summary>
        public Records Name(string name) =>
            Record(0x0016, Encoding.Latin1.GetBytes(name)).Record(0x003E, Encoding.Unicode.GetBytes(name));

        /// <summary>A REFERENCEREGISTERED record.</summary>
        public Records Registered(string libid) => Record(0x000D, [.. Counted(libid), .. new byte[6]]);

        /// <summary>A REFERENCECONTROL record with the extended half that follows it.</summary>
        public Records Control(string twiddled, string extended, string extendedName)
        {
            Record(0x002F, [.. Counted(twiddled), .. new byte[6]]);
            Name(extendedName);
            return Record(0x0030, [.. Counted(extended), .. new byte[6], .. new byte[16], .. new byte[4]]);
        }

        /// <summary>The PROJECTMODULES header, which is what ends the reference array.</summary>
        public Records EndOfModules() => Record(0x000F, 0x00, 0x00).Record(0x0013, 0xFF, 0xFF);

        public Records Module(string name, string? description, string? unicodeDescription)
        {
            Record(0x0019, Encoding.Latin1.GetBytes(name));
            Record(0x0047, Encoding.Unicode.GetBytes(name));
            Record(0x001A, Encoding.Latin1.GetBytes(name));
            Record(0x0032, Encoding.Unicode.GetBytes(name));
            Record(0x001C, Encoding.Latin1.GetBytes(description ?? string.Empty));
            Record(0x0048, Encoding.Unicode.GetBytes(unicodeDescription ?? string.Empty));
            Record(0x0031, Number(0));
            Record(0x001E, Number(0));
            Record(0x002C, 0xFF, 0xFF);
            Record(0x0021);
            return Record(0x002B);
        }

        /// <summary>A length ahead of the bytes it counts, as the reference records write it.</summary>
        private static byte[] Counted(string text)
        {
            byte[] value = Encoding.Latin1.GetBytes(text);
            return [.. Number(value.Length), .. value];
        }

        private static byte[] Number(int value)
        {
            byte[] bytes = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)value);
            return bytes;
        }
    }
}
