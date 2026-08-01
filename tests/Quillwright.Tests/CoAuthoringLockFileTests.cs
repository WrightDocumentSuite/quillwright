using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Quillwright.IO;

namespace Quillwright.Tests;

/// <summary>
/// The lock file that says who is editing which part of a shared document ([MS-WORDLFF]): a
/// deflated XML document behind an eight-byte signature, with its uncompressed length at the
/// end.
/// </summary>
public class CoAuthoringLockFileTests
{
    /// <summary>
    /// The example of [MS-WORDLFF] section 3, verbatim.
    /// </summary>
    /// <remarks>
    /// It is the only normative sample there is, and it is the one that catches the mistake:
    /// <c>Lock</c> carries <c>xmlns=""</c> and so is in no namespace, while <c>DeletedLocks</c>
    /// on the very next line inherits the root's. A reader that insists on either one alone
    /// comes back with half the file.
    /// </remarks>
    private const string Specimen =
        """
        <CoAuthoringLocks xmlns="http://schemas.microsoft.com/word/2009/7/coauthoring">
            <Lock xmlns="" OwnerID="{38A992A1-8CDB-4D8B-B881-7D7E45E06B72}"
        OwnerName="Claus Hansen" OwnerSIPAddress="sip:claus@example.com"
        OwnerEmailAddress="claus@example.com" OwnerUserName="claus" LockId="76224563">
                <ParaId Val="4F2EB091"/>
            </Lock>
            <Lock xmlns="" OwnerID="{33B5F63F-E6B4-41AA-B64E-552D8127DF2B}" OwnerName="Jeff
        Hay" OwnerSIPAddress="sip:jeff@example.com" OwnerEmailAddress="jeff@example.com"
        OwnerUserName="jeff" LockId="316786F3">
                <ParaId Val="4D3895E6"/>
                <ParaId Val="0EDB6FA0"/>
            </Lock>
            <DeletedLocks>
                <LockId Val="3F459ACD" TimeStamp="2009-05-14T00:18:14Z"/>
            </DeletedLocks>
        </CoAuthoringLocks>
        """;

    /// <summary>The same regions with every element qualified, which is what Word writes.</summary>
    private const string Qualified =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" +
        "<CoAuthoringLocks xmlns=\"http://schemas.microsoft.com/word/2009/7/coauthoring\">" +
        "<Lock LockId=\"0000002A\" OwnerID=\"{6B29FC40-CA47-1067-B31D-00DD010662DA}\" OwnerName=\"Ada Lovelace\" " +
        "OwnerUserName=\"ada@example.com\" OwnerEmailAddress=\"ada@example.com\">" +
        "<ParaId Val=\"1A2B3C4D\"/><ParaId Val=\"5E6F7081\"/></Lock>" +
        "<Lock LockId=\"0000002B\" OwnerID=\"{6B29FC40-CA47-1067-B31D-00DD010662DB}\" OwnerName=\"Grace Hopper\" " +
        "OwnerUserName=\"grace@example.com\"><ParaId Val=\"00ABCDEF\"/></Lock>" +
        "</CoAuthoringLocks>";

    [Fact]
    public void TheExampleOfSectionThree_SaysWhoHoldsWhichRegion()
    {
        IReadOnlyList<CoAuthoringLock> held = CoAuthoringLockFile.Read(Compress(Specimen))!;

        Assert.Equal(2, held.Count);
        Assert.Equal(3, held.Sum(static entry => entry.Paragraphs.Count));
        Assert.Equal(0x76224563u, held[0].Id);
        Assert.Equal("Claus Hansen", held[0].OwnerName);
        Assert.Equal("claus@example.com", held[0].OwnerEmailAddress);
        Assert.Equal("sip:claus@example.com", held[0].OwnerSipAddress);
        Assert.Equal([0x4F2EB091u], held[0].Paragraphs);
        Assert.Equal(0x316786F3u, held[1].Id);
        Assert.Equal([0x4D3895E6u, 0x0EDB6FA0u], held[1].Paragraphs);

        // The example wraps the second author's name across a line, and an XML parser turns
        // that newline into a space. Keeping the wrap is what makes the fixture verbatim.
        Assert.Equal("Jeff Hay", held[1].OwnerName);
    }

    [Fact]
    public void TheExampleOfSectionThree_AlsoSaysWhichRegionWasGivenUp()
    {
        CoAuthoringLocks file = CoAuthoringLockFile.ReadAll(Compress(Specimen))!;

        CoAuthoringDeletedLock withdrawn = Assert.Single(file.DeletedLocks);
        Assert.Equal(0x3F459ACDu, withdrawn.Id);
        Assert.Equal(new DateTimeOffset(2009, 5, 14, 0, 18, 14, TimeSpan.Zero), withdrawn.TimeStamp);
    }

    [Fact]
    public void ALockFileThatQualifiesItsChildren_IsReadAllTheSame()
    {
        IReadOnlyList<CoAuthoringLock> held = CoAuthoringLockFile.Read(Compress(Qualified))!;

        Assert.Equal(["Ada Lovelace", "Grace Hopper"], held.Select(static entry => entry.OwnerName));
        Assert.Equal([0x1A2B3C4Du, 0x5E6F7081u], held[0].Paragraphs);
        Assert.Equal([0x00ABCDEFu], held[1].Paragraphs);
    }

    [Fact]
    public void ALockInSomebodyElsesNamespace_IsNotOne()
    {
        byte[] bytes = Compress(
            "<CoAuthoringLocks xmlns=\"http://schemas.microsoft.com/word/2009/7/coauthoring\">" +
            "<Lock xmlns=\"urn:example:other\" LockId=\"0000002A\"><ParaId Val=\"1A2B3C4D\"/></Lock>" +
            "<Lock xmlns=\"\" LockId=\"0000002B\"><ParaId Val=\"00ABCDEF\"/></Lock>" +
            "</CoAuthoringLocks>");

        CoAuthoringLock only = Assert.Single(CoAuthoringLockFile.Read(bytes)!);
        Assert.Equal(0x2Bu, only.Id);
    }

    [Fact]
    public void ARootInTheWrongNamespace_IsNotALockFile()
    {
        byte[] bytes = Compress(
            "<CoAuthoringLocks xmlns=\"urn:example:other\">" +
            "<Lock xmlns=\"\" LockId=\"0000002A\"><ParaId Val=\"1A2B3C4D\"/></Lock>" +
            "</CoAuthoringLocks>");

        Assert.Null(CoAuthoringLockFile.Read(bytes));
        Assert.Null(CoAuthoringLockFile.ReadAll(bytes));
    }

    [Fact]
    public void SomethingThatIsNotALockFile_IsRefused()
    {
        Assert.False(CoAuthoringLockFile.IsLockFile("not a lock file at all"u8));
        Assert.Null(CoAuthoringLockFile.Read(Encoding.UTF8.GetBytes("not a lock file at all")));
    }

    [Fact]
    public void ALockFileWithDamagedBytes_IsRefusedRatherThanThrowing()
    {
        byte[] bytes = Compress(Qualified);
        bytes[20] ^= 0xFF;

        Assert.Null(CoAuthoringLockFile.Read(bytes));
    }

    [Fact]
    public void ALockFileWithNoLocksInIt_ComesBackEmpty()
    {
        byte[] bytes = Compress(
            "<CoAuthoringLocks xmlns=\"http://schemas.microsoft.com/word/2009/7/coauthoring\"/>");

        Assert.Empty(CoAuthoringLockFile.Read(bytes)!);
        Assert.Null(CoAuthoringLockFile.ReadAll(bytes)!.Sync);
    }

    /// <summary>Builds the stream of [MS-WORDLFF] 2.3 round the given markup.</summary>
    internal static byte[] Compress(string markup)
    {
        byte[] text = Encoding.UTF8.GetBytes(markup);
        using var deflated = new MemoryStream();
        using (var compressing = new ZLibStream(deflated, CompressionLevel.Optimal, leaveOpen: true))
            compressing.Write(text, 0, text.Length);

        var trailer = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(trailer.AsSpan(4), (uint)text.Length);
        return [0x1A, 0x5A, 0x3A, 0x30, 0x00, 0x00, 0x00, 0x00, .. deflated.ToArray(), .. trailer];
    }
}
