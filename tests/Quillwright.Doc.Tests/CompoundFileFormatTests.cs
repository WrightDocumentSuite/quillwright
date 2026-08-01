using System.Buffers.Binary;
using System.Text;
using Quillwright.Doc.Writing;
using Quillwright.IO;

namespace Quillwright.Doc.Tests;

/// <summary>
/// Corners of [MS-CFB] that the containers this library writes never reach, and that a reader
/// has to get right anyway because it is handed files other producers wrote.
/// </summary>
public class CompoundFileFormatTests
{
    private const uint EndOfChain = 0xFFFFFFFE;
    private const uint FreeSector = 0xFFFFFFFF;
    private const uint FatSector = 0xFFFFFFFD;
    private const uint NoStream = 0xFFFFFFFF;

    /// <summary>
    /// A version 4 container has 4096-byte sectors, so sector zero begins 4096 bytes in even
    /// though the header it follows is still only 512 bytes of fields. A reader that assumes
    /// the header is one sector because it usually is reads every sector from the wrong place.
    /// </summary>
    [Fact]
    public void AVersion4Container_IsReadFromTheRightOffsets()
    {
        byte[] big = Pattern(5000);
        byte[] small = Pattern(40);
        CompoundFile file = CompoundFile.Open(BuildVersion4(big, small));

        Assert.Equal(big, file.ReadStream("Big"));
        Assert.Equal(small, file.ReadStream("Small"));
        Assert.Equal(["Big", "Small"], file.StreamNames.Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// The size of a stream is eight bytes, but a version 3 container can only hold two
    /// gigabytes and older writers left the top four bytes as they found them. Reading them
    /// would make a small stream look enormous and send the reader to the wrong allocation.
    /// </summary>
    [Fact]
    public void AStreamSizeWithRubbishInItsTopHalf_IsStillReadFromTheMiniAllocation()
    {
        // The two streams must not share a prefix: read from the wrong allocation, the small
        // one lands on the front of the big one, and identical bytes would hide the mistake.
        var writer = new CompoundFileWriter();
        byte[] table = Pattern(300, seed: 7);
        writer.Add("WordDocument", Pattern(9000));
        writer.Add("1Table", table);

        byte[] container = writer.Build();
        int entry = FindEntry(container, "1Table");
        BinaryPrimitives.WriteUInt32LittleEndian(container.AsSpan(entry + 124), 0xDEADBEEF);

        Assert.Equal(table, CompoundFile.Open(container).ReadStream("1Table"));
    }

    /// <summary>
    /// A directory sector holds four entries whether or not there are four things to name, and
    /// the ones left over have to say they point at nothing rather than at entry zero.
    /// </summary>
    [Fact]
    public void DirectoryEntriesThatNameNothing_PointAtNothing()
    {
        var writer = new CompoundFileWriter();
        writer.Add("WordDocument", Pattern(1000));
        byte[] container = writer.Build();

        int directory = 512 + (512 * (int)BinaryPrimitives.ReadUInt32LittleEndian(container.AsSpan(48)));
        for (int i = 2; i < 4; i++)
        {
            ReadOnlySpan<byte> entry = container.AsSpan(directory + (i * 128), 128);

            Assert.Equal(0, entry[66]);
            Assert.Equal(NoStream, BinaryPrimitives.ReadUInt32LittleEndian(entry[68..]));
            Assert.Equal(NoStream, BinaryPrimitives.ReadUInt32LittleEndian(entry[72..]));
            Assert.Equal(NoStream, BinaryPrimitives.ReadUInt32LittleEndian(entry[76..]));
        }
    }

    /// <summary>
    /// The container [MS-CFB] section 3 works through byte by byte: a storage holding one
    /// stream of 544 bytes, which is short enough to live in the mini allocation. It is the
    /// one check here that owes nothing to a file this library or Word produced.
    /// </summary>
    [Fact]
    public void TheWorkedExample_IsReadTheWayTheSpecificationDescribesIt()
    {
        CompoundFile file = CompoundFile.Open(BuildWorkedExample());

        Assert.True(file.HasStorage("Storage 1"));
        Assert.Equal(["Storage 1/Stream 1"], file.StreamNames);
        Assert.Equal(
            Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("Data for stream 1", 32))),
            file.ReadStream("Storage 1/Stream 1"));
    }

    /// <summary>
    /// Assembles the container of [MS-CFB] section 3 from the field tables and hex dumps
    /// printed there: a header, one FAT sector, one directory sector of four entries, one mini
    /// FAT sector, and two sectors of mini stream.
    /// </summary>
    private static byte[] BuildWorkedExample()
    {
        var file = new byte[512 * 6];
        Span<byte> header = file.AsSpan(0, 512);
        ((ReadOnlySpan<byte>)[0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]).CopyTo(header);
        BinaryPrimitives.WriteUInt16LittleEndian(header[24..], 0x003E);
        BinaryPrimitives.WriteUInt16LittleEndian(header[26..], 0x0003);
        BinaryPrimitives.WriteUInt16LittleEndian(header[28..], 0xFFFE);
        BinaryPrimitives.WriteUInt16LittleEndian(header[30..], 0x0009);
        BinaryPrimitives.WriteUInt16LittleEndian(header[32..], 0x0006);
        BinaryPrimitives.WriteUInt32LittleEndian(header[44..], 1);
        BinaryPrimitives.WriteUInt32LittleEndian(header[48..], 1);
        BinaryPrimitives.WriteUInt32LittleEndian(header[56..], 0x1000);
        BinaryPrimitives.WriteUInt32LittleEndian(header[60..], 2);
        BinaryPrimitives.WriteUInt32LittleEndian(header[64..], 1);
        BinaryPrimitives.WriteUInt32LittleEndian(header[68..], EndOfChain);
        for (int i = 0; i < 109; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(header[(76 + (i * 4))..], i == 0 ? 0u : FreeSector);

        // Sector 0: the table itself, the directory, the mini FAT, then the two sectors the
        // mini stream runs across.
        Table(file.AsSpan(512, 512), [FatSector, EndOfChain, EndOfChain, 4, EndOfChain]);

        // Sector 2: nine mini sectors chained end to end for the one stream in the file.
        Table(file.AsSpan(512 * 3, 512), [1, 2, 3, 4, 5, 6, 7, 8, EndOfChain]);

        Span<byte> directory = file.AsSpan(512 * 2, 512);
        for (int i = 0; i < 4; i++)
        {
            Span<byte> blank = directory[(i * 128)..];
            BinaryPrimitives.WriteUInt32LittleEndian(blank[68..], NoStream);
            BinaryPrimitives.WriteUInt32LittleEndian(blank[72..], NoStream);
            BinaryPrimitives.WriteUInt32LittleEndian(blank[76..], NoStream);
        }

        Entry(directory, 0, "Root Entry", kind: 5, start: 3, size: 576, right: NoStream, child: 1);
        Entry(directory, 1, "Storage 1", kind: 1, start: 0, size: 0, right: NoStream, child: 2);
        Entry(directory, 2, "Stream 1", kind: 2, start: 0, size: 544, right: NoStream, child: NoStream);

        Encoding.ASCII.GetBytes(string.Concat(Enumerable.Repeat("Data for stream 1", 32)))
            .CopyTo(file.AsSpan(512 * 4));
        return file;

        static void Table(Span<byte> sector, uint[] head)
        {
            for (int i = 0; i < sector.Length / 4; i++)
                BinaryPrimitives.WriteUInt32LittleEndian(sector[(i * 4)..], i < head.Length ? head[i] : FreeSector);
        }
    }

    /// <summary>
    /// Builds a version 4 container by hand: 4096-byte sectors, one stream too big for the
    /// mini allocation and one small enough for it.
    /// </summary>
    /// <param name="big">Contents of the stream allocated from the main table.</param>
    /// <param name="small">Contents of the stream allocated from the mini table.</param>
    private static byte[] BuildVersion4(byte[] big, byte[] small)
    {
        const int Sector = 4096;
        const int Sectors = 6;

        // 0: the allocation table. 1: the directory. 2 and 3: the big stream. 4: the mini
        // stream the small one lives inside. 5: the mini allocation table.
        var file = new byte[Sector * (1 + Sectors)];
        Span<byte> header = file.AsSpan(0, Sector);
        ((ReadOnlySpan<byte>)[0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]).CopyTo(header);
        BinaryPrimitives.WriteUInt16LittleEndian(header[24..], 0x003E);
        BinaryPrimitives.WriteUInt16LittleEndian(header[26..], 0x0004);
        BinaryPrimitives.WriteUInt16LittleEndian(header[28..], 0xFFFE);
        BinaryPrimitives.WriteUInt16LittleEndian(header[30..], 0x000C);
        BinaryPrimitives.WriteUInt16LittleEndian(header[32..], 0x0006);
        BinaryPrimitives.WriteUInt32LittleEndian(header[40..], 1);
        BinaryPrimitives.WriteUInt32LittleEndian(header[44..], 1);
        BinaryPrimitives.WriteUInt32LittleEndian(header[48..], 1);
        BinaryPrimitives.WriteUInt32LittleEndian(header[56..], 0x1000);
        BinaryPrimitives.WriteUInt32LittleEndian(header[60..], 5);
        BinaryPrimitives.WriteUInt32LittleEndian(header[64..], 1);
        BinaryPrimitives.WriteUInt32LittleEndian(header[68..], EndOfChain);
        for (int i = 0; i < 109; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(header[(76 + (i * 4))..], i == 0 ? 0u : FreeSector);

        Span<byte> fat = file.AsSpan(Offset(0), Sector);
        for (int i = 0; i < Sector / 4; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(fat[(i * 4)..], FreeSector);
        Write(fat, 0, FatSector);
        Write(fat, 1, EndOfChain);
        Write(fat, 2, 3);
        Write(fat, 3, EndOfChain);
        Write(fat, 4, EndOfChain);
        Write(fat, 5, EndOfChain);

        Span<byte> miniFat = file.AsSpan(Offset(5), Sector);
        for (int i = 0; i < Sector / 4; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(miniFat[(i * 4)..], FreeSector);
        Write(miniFat, 0, EndOfChain);

        big.CopyTo(file.AsSpan(Offset(2)));
        small.CopyTo(file.AsSpan(Offset(4)));

        Span<byte> directory = file.AsSpan(Offset(1), Sector);
        for (int i = 0; i < Sector / 128; i++)
        {
            Span<byte> blank = directory[(i * 128)..];
            BinaryPrimitives.WriteUInt32LittleEndian(blank[68..], NoStream);
            BinaryPrimitives.WriteUInt32LittleEndian(blank[72..], NoStream);
            BinaryPrimitives.WriteUInt32LittleEndian(blank[76..], NoStream);
        }

        // The root owns the mini stream: one mini sector of it, holding the small stream.
        Entry(directory, 0, "Root Entry", kind: 5, start: 4, size: 64, right: NoStream, child: 1);
        Entry(directory, 1, "Big", kind: 2, start: 2, size: big.Length, right: 2, child: NoStream);
        Entry(directory, 2, "Small", kind: 2, start: 0, size: small.Length, right: NoStream, child: NoStream);
        return file;

        static int Offset(int sector) => Sector * (sector + 1);
        static void Write(Span<byte> table, int index, uint value) =>
            BinaryPrimitives.WriteUInt32LittleEndian(table[(index * 4)..], value);
    }

    private static void Entry(Span<byte> directory, int index, string name, byte kind, int start, long size, uint right, uint child)
    {
        Span<byte> entry = directory.Slice(index * 128, 128);
        int bytes = Encoding.Unicode.GetBytes(name, entry[..62]);
        BinaryPrimitives.WriteUInt16LittleEndian(entry[64..], (ushort)(bytes + 2));
        entry[66] = kind;
        entry[67] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(entry[68..], NoStream);
        BinaryPrimitives.WriteUInt32LittleEndian(entry[72..], right);
        BinaryPrimitives.WriteUInt32LittleEndian(entry[76..], child);
        BinaryPrimitives.WriteUInt32LittleEndian(entry[116..], (uint)start);
        BinaryPrimitives.WriteUInt64LittleEndian(entry[120..], (ulong)size);
    }

    /// <summary>Where a named stream's directory entry begins, in a version 3 container.</summary>
    private static int FindEntry(byte[] container, string name)
    {
        int directory = 512 + (512 * (int)BinaryPrimitives.ReadUInt32LittleEndian(container.AsSpan(48)));
        for (int i = 0; directory + ((i + 1) * 128) <= container.Length; i++)
        {
            int at = directory + (i * 128);
            int length = BinaryPrimitives.ReadUInt16LittleEndian(container.AsSpan(at + 64));
            if (length is >= 2 and <= 64 && Encoding.Unicode.GetString(container, at, length - 2) == name)
                return at;
        }

        Assert.Fail($"no directory entry named {name}");
        return -1;
    }

    private static byte[] Pattern(int length, int seed = 0)
    {
        var bytes = new byte[length];
        for (int i = 0; i < length; i++)
            bytes[i] = (byte)((i * 31) + (i / 251) + (seed * 101) + 1);
        return bytes;
    }
}
