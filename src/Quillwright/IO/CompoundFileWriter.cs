using System.Buffers.Binary;
using System.Text;

namespace Quillwright.IO;

/// <summary>
/// Writes a Compound File Binary container ([MS-CFB]) — the little file system a
/// <c>.doc</c> is stored in.
/// </summary>
/// <remarks>
/// <para>
/// Streams under 4096 bytes do not get sectors of their own. They live end to end inside a
/// second, finer allocation called the mini stream, which is itself an ordinary stream owned
/// by the root entry. A Word document always has at least one small stream — the table
/// stream of a short document — so a writer that only knows the coarse allocation produces
/// files no reader can open.
/// </para>
/// <para>
/// The one circular part of the layout is the allocation table: it has to describe the
/// sectors it is stored in, so its own size depends on the total, which depends on its size.
/// That is settled by iterating to a fixed point, which converges in two or three rounds.
/// </para>
/// </remarks>
internal sealed partial class CompoundFileWriter
{
    private const int SectorSize = 512;
    private const int MiniSectorSize = 64;
    private const int MiniStreamCutoff = 4096;
    private const int DirectoryEntrySize = 128;
    private const int FatEntriesPerSector = SectorSize / 4;

    private const uint FreeSector = 0xFFFFFFFF;
    private const uint EndOfChain = 0xFFFFFFFE;
    private const uint FatSector = 0xFFFFFFFD;
    private const uint DifatSector = 0xFFFFFFFC;

    /// <summary>
    /// The terminator of a directory tree pointer ([MS-CFB] 2.6.1). It is the same value as
    /// <see cref="FreeSector"/> and means something else: no entry rather than no sector.
    /// </summary>
    private const uint NoStream = 0xFFFFFFFF;

    private readonly List<(string Name, byte[] Content)> _streams = [];

    /// <summary>Adds a stream to the root storage. Names must be unique and at most 31 characters.</summary>
    /// <param name="name">Stream name as it appears in the directory.</param>
    /// <param name="content">The bytes of the stream.</param>
    public void Add(string name, byte[] content)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(content);
        if (name.Length > 31)
            throw new ArgumentException("A compound file stream name may hold at most 31 characters.", nameof(name));

        _streams.Add((name, content));
    }

    /// <summary>Exact output length for the streams currently registered, without laying out buffers.</summary>
    public long EstimateBuildLength()
    {
        long regularSectors = 0;
        long miniSectors = 0;
        foreach ((_, byte[] content) in _streams)
        {
            if (content.Length < MiniStreamCutoff)
                miniSectors += Ceiling(content.Length, MiniSectorSize);
            else
                regularSectors += Ceiling(content.Length, SectorSize);
        }

        long dataSectors = regularSectors +
                           Ceiling(miniSectors * MiniSectorSize, SectorSize) +
                           Ceiling(miniSectors, FatEntriesPerSector) +
                           Ceiling(_streams.Count + 1L, SectorSize / DirectoryEntrySize);
        (long fat, long difat) = CountFatSectors(dataSectors);
        return checked(SectorSize * (1 + dataSectors + fat + difat));
    }

    /// <summary>Builds the container.</summary>
    public byte[] Build()
    {
        var layout = new Layout(_streams);
        var file = new byte[SectorSize * (1 + layout.TotalSectors)];

        WriteHeader(file, layout);
        layout.WriteStreamData(file);
        layout.WriteMiniFat(file);
        layout.WriteDirectory(file);
        layout.WriteFat(file);
        layout.WriteDifat(file);
        return file;
    }

    private static (long Fat, long Difat) CountFatSectors(long dataSectors)
    {
        long fat = 0;
        long difat = 0;
        for (int round = 0; round < 8; round++)
        {
            long nextFat = Ceiling(dataSectors + fat + difat, FatEntriesPerSector);
            long nextDifat = nextFat <= 109 ? 0 : Ceiling(nextFat - 109, FatEntriesPerSector - 1);
            if (nextFat == fat && nextDifat == difat)
                break;
            (fat, difat) = (nextFat, nextDifat);
        }

        return (fat, difat);
    }

    private static long Ceiling(long value, long unit) => value <= 0 ? 0 : ((value - 1) / unit) + 1;

    private static void WriteHeader(byte[] file, Layout layout)
    {
        Span<byte> header = file.AsSpan(0, SectorSize);
        header.Fill(0);
        ((ReadOnlySpan<byte>)[0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]).CopyTo(header);

        BinaryPrimitives.WriteUInt16LittleEndian(header[24..], 0x003E);
        BinaryPrimitives.WriteUInt16LittleEndian(header[26..], 0x0003);
        BinaryPrimitives.WriteUInt16LittleEndian(header[28..], 0xFFFE);
        BinaryPrimitives.WriteUInt16LittleEndian(header[30..], 9);
        BinaryPrimitives.WriteUInt16LittleEndian(header[32..], 6);
        BinaryPrimitives.WriteUInt32LittleEndian(header[44..], (uint)layout.FatSectorCount);
        BinaryPrimitives.WriteUInt32LittleEndian(header[48..], (uint)layout.DirectoryStart);
        BinaryPrimitives.WriteUInt32LittleEndian(header[56..], MiniStreamCutoff);
        BinaryPrimitives.WriteUInt32LittleEndian(header[60..],
            layout.MiniFatSectorCount == 0 ? EndOfChain : (uint)layout.MiniFatStart);
        BinaryPrimitives.WriteUInt32LittleEndian(header[64..], (uint)layout.MiniFatSectorCount);
        BinaryPrimitives.WriteUInt32LittleEndian(header[68..],
            layout.DifatSectorCount == 0 ? EndOfChain : (uint)layout.DifatStart);
        BinaryPrimitives.WriteUInt32LittleEndian(header[72..], (uint)layout.DifatSectorCount);

        for (int i = 0; i < 109; i++)
        {
            uint value = i < layout.FatSectorCount ? (uint)(layout.FatStart + i) : FreeSector;
            BinaryPrimitives.WriteUInt32LittleEndian(header[(76 + (i * 4))..], value);
        }
    }

}
