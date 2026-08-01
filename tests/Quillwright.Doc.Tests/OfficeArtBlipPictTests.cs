using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Quillwright.Model;

namespace Quillwright.Doc.Tests;

/// <summary>
/// The Macintosh PICT blip ([MS-ODRAW] 2.2.26): record type <c>0xF01C</c>, one unique
/// identifier at <c>recInstance</c> <c>0x542</c> and two at <c>0x543</c>, then the same
/// 34-byte metafile header the EMF and WMF blips use.
/// </summary>
/// <remarks>
/// Reading is all this does. The <c>.doc</c> writer authors PNG and JPEG records only, so a
/// PICT survives a conversion as media bytes in the package — which a renderer that has never
/// heard of QuickDraw will not draw, and which is still better than losing it.
/// </remarks>
public class OfficeArtBlipPictTests
{
    private const ushort Pict = 0xF01C;
    private const int OneIdentity = 0x542;
    private const int TwoIdentities = 0x543;

    /// <summary>Something that is not a format the sniffer knows, so only the record can name it.</summary>
    private static byte[] Payload => Encoding.ASCII.GetBytes(
        "\u0000\u0000PICTdrawing bytes that nothing here pretends to understand");

    [Theory]
    [InlineData(OneIdentity, true)]
    [InlineData(OneIdentity, false)]
    [InlineData(TwoIdentities, true)]
    [InlineData(TwoIdentities, false)]
    public void APictRecord_GivesBackExactlyTheDrawingItWasBuiltFrom(int instance, bool deflated)
    {
        byte[] record = Blip(instance, deflated, Payload);

        ImageData image = Read(record)!;

        Assert.Equal("image/pict", image.ContentType);
        Assert.Equal("pict", image.Extension);
        Assert.Equal(Payload, image.Bytes.ToArray());
    }

    /// <summary>
    /// The second identifier is 16 bytes the reader has to step over. Getting that wrong does
    /// not fail loudly — it hands back a drawing that begins sixteen bytes early or late — so
    /// the two layouts are compared against each other rather than only checked for length.
    /// </summary>
    [Fact]
    public void TheTwoIdentityLayout_StartsTheDrawingSixteenBytesLater()
    {
        byte[] one = Blip(OneIdentity, deflated: false, Payload);
        byte[] two = Blip(TwoIdentities, deflated: false, Payload);

        Assert.Equal(one.Length + 16, two.Length);
        Assert.Equal(Read(one)!.Bytes.ToArray(), Read(two)!.Bytes.ToArray());
    }

    /// <summary>An instance the specification does not define carries one identifier, not two.</summary>
    [Fact]
    public void AnInstanceThatIsNeither_IsReadAsTheSingleIdentityLayout()
    {
        byte[] record = Blip(0x000, deflated: false, Payload);

        Assert.Equal(Payload, Read(record)!.Bytes.ToArray());
    }

    /// <summary>
    /// A store entry that carries its blip inside itself ([MS-ODRAW] 2.2.32), which is how a
    /// <c>.doc</c> actually holds one.
    /// </summary>
    [Fact]
    public void APictInsideAStoreEntry_IsResolvedThroughIt()
    {
        byte[] blip = Blip(TwoIdentities, deflated: true, Payload);
        byte[] entry = StoreEntry(blip);

        Assert.True(OfficeArtRecord.TryRead(entry, 0, entry.Length, out OfficeArtRecord record));
        ImageData image = OfficeArtBlip.Resolve(entry, record, delayed: null)!;

        Assert.Equal("image/pict", image.ContentType);
        Assert.Equal(Payload, image.Bytes.ToArray());
    }

    [Fact]
    public void APictExtension_AndTheContentTypeOfOne_MapOntoEachOther()
    {
        Assert.Equal("image/pict", ImageData.ContentTypeForExtension("pict"));
        Assert.Equal("pict", ImageData.ExtensionForContentType("image/pict"));
        Assert.Equal("pict", ImageData.ExtensionForContentType("image/x-pict"));
    }

    private static ImageData? Read(byte[] record)
    {
        Assert.True(OfficeArtRecord.TryRead(record, 0, record.Length, out OfficeArtRecord blip));
        Assert.Equal(Pict, blip.Type);
        return OfficeArtBlip.Read(record, blip);
    }

    /// <summary>Builds one <c>OfficeArtBlipPICT</c> exactly as [MS-ODRAW] 2.2.26 lays it out.</summary>
    private static byte[] Blip(int instance, bool deflated, byte[] drawing)
    {
        byte[] stored = deflated ? Deflate(drawing) : drawing;
        int identities = instance == TwoIdentities ? 32 : 16;

        var record = new byte[OfficeArtRecord.HeaderBytes + identities + 34 + stored.Length];
        Span<byte> bytes = record;

        BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)(instance << 4));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[2..], Pict);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[4..], identities + 34 + stored.Length);

        // Digests of the drawing, which the reader has no use for and only has to step over.
        for (int i = 0; i < identities; i++)
            record[OfficeArtRecord.HeaderBytes + i] = (byte)(0xA0 + i);

        Span<byte> header = bytes[(OfficeArtRecord.HeaderBytes + identities)..];
        BinaryPrimitives.WriteInt32LittleEndian(header, drawing.Length);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], 0);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..], 0);
        BinaryPrimitives.WriteInt32LittleEndian(header[12..], 8000);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..], 6000);
        BinaryPrimitives.WriteInt32LittleEndian(header[20..], 2286000);
        BinaryPrimitives.WriteInt32LittleEndian(header[24..], 1714500);
        BinaryPrimitives.WriteInt32LittleEndian(header[28..], stored.Length);
        header[32] = deflated ? (byte)0x00 : (byte)0xFE;
        header[33] = 0xFE;

        stored.CopyTo(record.AsSpan(OfficeArtRecord.HeaderBytes + identities + 34));
        return record;
    }

    /// <summary>Wraps a blip in the store entry that names it (<c>btMacOS</c> is PICT, 0x04).</summary>
    private static byte[] StoreEntry(byte[] blip)
    {
        const int EntryHeaderBytes = 36;
        var entry = new byte[OfficeArtRecord.HeaderBytes + EntryHeaderBytes + blip.Length];
        Span<byte> bytes = entry;

        BinaryPrimitives.WriteUInt16LittleEndian(bytes, (0x04 << 4) | 0x2);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes[2..], 0xF007);
        BinaryPrimitives.WriteInt32LittleEndian(bytes[4..], EntryHeaderBytes + blip.Length);

        Span<byte> body = bytes[OfficeArtRecord.HeaderBytes..];
        body[0] = 0x04;
        body[1] = 0x04;
        BinaryPrimitives.WriteInt32LittleEndian(body[20..], blip.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(body[28..], 0xFFFFFFFF);
        body[33] = 0;

        blip.CopyTo(entry.AsSpan(OfficeArtRecord.HeaderBytes + EntryHeaderBytes));
        return entry;
    }

    private static byte[] Deflate(byte[] drawing)
    {
        using var deflated = new MemoryStream();
        using (var compressing = new ZLibStream(deflated, CompressionLevel.Optimal, leaveOpen: true))
            compressing.Write(drawing, 0, drawing.Length);
        return deflated.ToArray();
    }
}
