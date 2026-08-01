using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Quillwright.IO;

namespace Quillwright.Tests;

/// <summary>
/// The rest of <c>CT_CALocks</c> ([MS-WORDLFF] 2.4.3.1): the eight child categories that are
/// not the committed locks, and the rules for reading identifiers and timestamps.
/// </summary>
public class CoAuthoringLocksModelTests
{
    private static byte[] File(string children) =>
        CoAuthoringLockFileTests.Compress(
            "<CoAuthoringLocks xmlns=\"http://schemas.microsoft.com/word/2009/7/coauthoring\">" +
            children + "</CoAuthoringLocks>");

    private static CoAuthoringLocks Read(string children) => CoAuthoringLockFile.ReadAll(File(children))!;

    [Fact]
    public void ASynchronisationRequest_CarriesBothIdentifiersAndTheRevision()
    {
        CoAuthoringSync sync = Read(
            "<Sync xmlns=\"\" DocID=\"11223344\" NextID=\"11223350\" RevisionID=\"{7F1B}-42\"/>").Sync!;

        Assert.Equal(0x11223344u, sync.DocumentId);
        Assert.Equal(0x11223350u, sync.NextId);
        Assert.Equal("{7F1B}-42", sync.RevisionId);
    }

    [Fact]
    public void TheThreeKindsOfLock_AreKeptApart()
    {
        CoAuthoringLocks file = Read(
            "<Lock xmlns=\"\" LockId=\"0000000A\"><ParaId Val=\"0000001A\"/></Lock>" +
            "<UncommittedLock xmlns=\"\" LockId=\"0000000B\"><ParaId Val=\"0000001B\"/></UncommittedLock>" +
            "<EphemeralLock xmlns=\"\" LockId=\"0000000C\"><ParaId Val=\"0000001C\"/></EphemeralLock>");

        Assert.Equal(0x0Au, Assert.Single(file.Locks).Id);
        Assert.Equal(0x0Bu, Assert.Single(file.UncommittedLocks).Id);
        Assert.Equal(0x0Cu, Assert.Single(file.EphemeralLocks).Id);
    }

    [Fact]
    public void ALockWhoseIdentifierWasGivenUp_IsNotInForceButIsStillInTheFile()
    {
        CoAuthoringLocks file = Read(
            "<Lock xmlns=\"\" LockId=\"0000000A\"><ParaId Val=\"0000001A\"/></Lock>" +
            "<Lock xmlns=\"\" LockId=\"0000000B\"><ParaId Val=\"0000001B\"/></Lock>" +
            "<DeletedLocks xmlns=\"\"><LockId Val=\"0000000A\" TimeStamp=\"2009-05-14T00:18:14Z\"/></DeletedLocks>");

        Assert.Equal([0x0Au, 0x0Bu], file.Locks.Select(static held => held.Id));
        Assert.Equal(0x0Bu, Assert.Single(file.Effective).Id);
        Assert.Equal(0x0Au, Assert.Single(file.DeletedLocks).Id);
    }

    [Fact]
    public void ThePruneTimeAndTheIdentifierLists_AreReadInFull()
    {
        CoAuthoringLocks file = Read(
            "<IDPruneTime xmlns=\"\" TimeStamp=\"2009-05-14T00:18:14Z\"/>" +
            "<AutoDeletableLocks xmlns=\"\"><LockId Val=\"0000000A\"/><LockId Val=\"0000000B\"/></AutoDeletableLocks>" +
            "<MakePlaceholder xmlns=\"\"><LockId Val=\"0000000C\"/></MakePlaceholder>");

        Assert.Equal(new DateTimeOffset(2009, 5, 14, 0, 18, 14, TimeSpan.Zero), file.IdPruneTime);
        Assert.Equal([0x0Au, 0x0Bu], file.AutoDeletableLocks);
        Assert.Equal([0x0Cu], file.MakePlaceholder);
    }

    [Fact]
    public void AnAuthorWhoseDetailsChanged_KeepsEveryFieldIncludingTheSipAddress()
    {
        CoAuthoringLockOwner owner = Assert.Single(Read(
            "<UserInfoChanges xmlns=\"\"><UserInfoChange OwnerID=\"{38A992A1-8CDB-4D8B-B881-7D7E45E06B72}\" " +
            "OwnerName=\"Claus Hansen\" OwnerSIPAddress=\"sip:claus@example.com\" " +
            "OwnerEmailAddress=\"claus@example.com\" OwnerUserName=\"claus\"/></UserInfoChanges>").UserInfoChanges);

        Assert.Equal("{38A992A1-8CDB-4D8B-B881-7D7E45E06B72}", owner.OwnerId);
        Assert.Equal("Claus Hansen", owner.OwnerName);
        Assert.Equal("sip:claus@example.com", owner.OwnerSipAddress);
        Assert.Equal("claus@example.com", owner.OwnerEmailAddress);
        Assert.Equal("claus", owner.OwnerUserName);
    }

    [Theory]
    [InlineData("00000000")]
    [InlineData("0")]
    [InlineData("2A")]
    [InlineData("0000002AB")]
    [InlineData("0x00002A")]
    [InlineData("ZZZZZZZZ")]
    [InlineData("")]
    public void AnIdentifierThatIsNotFourHexadecimalBytes_DropsTheRecordRatherThanBecomingZero(string value)
    {
        CoAuthoringLocks file = Read(
            $"<Lock xmlns=\"\" LockId=\"{value}\"><ParaId Val=\"0000001A\"/></Lock>" +
            "<Lock xmlns=\"\" LockId=\"0000000B\"><ParaId Val=\"0000001B\"/></Lock>");

        Assert.Equal(0x0Bu, Assert.Single(file.Locks).Id);
    }

    [Fact]
    public void AParagraphIdentifierThatWillNotParse_IsDroppedWithoutLosingTheLock()
    {
        CoAuthoringLock held = Assert.Single(Read(
            "<Lock xmlns=\"\" LockId=\"0000000A\"><ParaId Val=\"00000000\"/><ParaId Val=\"0000001B\"/></Lock>").Locks);

        Assert.Equal([0x1Bu], held.Paragraphs);
    }

    [Fact]
    public void ATimestampWithAnOffset_KeepsTheOffsetItWasWrittenWith()
    {
        CoAuthoringDeletedLock withdrawn = Assert.Single(Read(
            "<DeletedLocks xmlns=\"\"><LockId Val=\"0000000A\" TimeStamp=\"2009-05-14T02:18:14+02:00\"/></DeletedLocks>")
            .DeletedLocks);

        Assert.Equal(TimeSpan.FromHours(2), withdrawn.TimeStamp!.Value.Offset);
        Assert.Equal(new DateTimeOffset(2009, 5, 14, 0, 18, 14, TimeSpan.Zero), withdrawn.TimeStamp!.Value.ToUniversalTime());
    }

    [Fact]
    public void ATimestampThatWillNotParse_StillWithdrawsTheIdentifier()
    {
        CoAuthoringLocks file = Read(
            "<Lock xmlns=\"\" LockId=\"0000000A\"><ParaId Val=\"0000001A\"/></Lock>" +
            "<DeletedLocks xmlns=\"\"><LockId Val=\"0000000A\" TimeStamp=\"the fourteenth\"/></DeletedLocks>");

        Assert.Null(Assert.Single(file.DeletedLocks).TimeStamp);
        Assert.Empty(file.Effective);
    }

    [Fact]
    public void AnElementNobodyHasDefinedYet_IsSteppedOverAlongWithWhatIsInside()
    {
        CoAuthoringLocks file = Read(
            "<Lock xmlns=\"\" LockId=\"0000000A\"><ParaId Val=\"0000001A\"/></Lock>" +
            "<FutureThing xmlns=\"\"><Lock LockId=\"0000000B\"><ParaId Val=\"0000001B\"/></Lock>" +
            "<DeletedLocks><LockId Val=\"0000000A\" TimeStamp=\"2009-05-14T00:18:14Z\"/></DeletedLocks></FutureThing>" +
            "<Lock xmlns=\"\" LockId=\"0000000C\"><ParaId Val=\"0000001C\"/></Lock>");

        Assert.Equal([0x0Au, 0x0Cu], file.Locks.Select(static held => held.Id));
        Assert.Empty(file.DeletedLocks);
    }

    [Fact]
    public void TheOrderOfTheFile_IsTheOrderOfEachCollection()
    {
        CoAuthoringLocks file = Read(
            "<Lock xmlns=\"\" LockId=\"0000000C\"><ParaId Val=\"0000003C\"/><ParaId Val=\"0000001C\"/></Lock>" +
            "<Lock xmlns=\"\" LockId=\"0000000A\"><ParaId Val=\"0000001A\"/></Lock>" +
            "<Lock xmlns=\"\" LockId=\"0000000B\"><ParaId Val=\"0000001B\"/></Lock>");

        Assert.Equal([0x0Cu, 0x0Au, 0x0Bu], file.Locks.Select(static held => held.Id));
        Assert.Equal([0x3Cu, 0x1Cu], file.Locks[0].Paragraphs);
    }

    [Fact]
    public void ATrailerThatDisagreesWithWhatExpands_IsRefused()
    {
        byte[] bytes = File("<Lock xmlns=\"\" LockId=\"0000000A\"><ParaId Val=\"0000001A\"/></Lock>");
        uint declared = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(bytes.Length - 4));

        Assert.NotNull(CoAuthoringLockFile.ReadAll(bytes));
        Assert.Null(CoAuthoringLockFile.ReadAll(WithDeclaredLength(bytes, declared - 1)));
        Assert.Null(CoAuthoringLockFile.ReadAll(WithDeclaredLength(bytes, declared + 1)));
    }

    [Fact]
    public void ATrailerDemandingMoreThanTheReaderWillExpand_IsRefusedBeforeAnythingIsAllocated()
    {
        byte[] bytes = File("<Lock xmlns=\"\" LockId=\"0000000A\"><ParaId Val=\"0000001A\"/></Lock>");

        Assert.Null(CoAuthoringLockFile.ReadAll(WithDeclaredLength(bytes, uint.MaxValue)));
        Assert.Null(CoAuthoringLockFile.ReadAll(WithDeclaredLength(bytes, 0)));
    }

    [Fact]
    public void TheFourReservedTrailerBytes_AreIgnoredAsTheSpecificationRequires()
    {
        byte[] bytes = File("<Lock xmlns=\"\" LockId=\"0000000A\"><ParaId Val=\"0000001A\"/></Lock>");
        Encoding.ASCII.GetBytes("junk").CopyTo(bytes.AsSpan(bytes.Length - 8));

        Assert.Equal(0x0Au, Assert.Single(CoAuthoringLockFile.ReadAll(bytes)!.Locks).Id);
    }

    /// <summary>A quine of a decompression bomb: a megabyte of zeroes that deflates to nothing.</summary>
    [Fact]
    public void AStreamThatExpandsFarBeyondItsTrailer_StopsAtWhatTheTrailerSaid()
    {
        byte[] payload = new byte[4 * 1024 * 1024];
        using var deflated = new MemoryStream();
        using (var compressing = new ZLibStream(deflated, CompressionLevel.Optimal, leaveOpen: true))
            compressing.Write(payload, 0, payload.Length);

        var trailer = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(trailer.AsSpan(4), 64u);
        byte[] bytes = [0x1A, 0x5A, 0x3A, 0x30, 0x00, 0x00, 0x00, 0x00, .. deflated.ToArray(), .. trailer];

        Assert.Null(CoAuthoringLockFile.ReadAll(bytes));
    }

    private static byte[] WithDeclaredLength(byte[] bytes, uint length)
    {
        byte[] copy = [.. bytes];
        BinaryPrimitives.WriteUInt32LittleEndian(copy.AsSpan(copy.Length - 4), length);
        return copy;
    }
}
