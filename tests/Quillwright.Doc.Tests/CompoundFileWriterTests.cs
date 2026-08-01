using Quillwright.Doc.Writing;

namespace Quillwright.Doc.Tests;

public class CompoundFileWriterTests
{
    [Fact]
    public void SmallStreams_GoThroughTheMiniAllocationAndComeBack()
    {
        var writer = new CompoundFileWriter();
        byte[] table = Pattern(300);
        byte[] document = Pattern(1500);

        writer.Add("WordDocument", document);
        writer.Add("1Table", table);

        CompoundFile reopened = CompoundFile.Open(writer.Build());

        Assert.Equal(document, reopened.ReadStream("WordDocument"));
        Assert.Equal(table, reopened.ReadStream("1Table"));
    }

    [Fact]
    public void LargeStreams_GoThroughTheCoarseAllocation()
    {
        var writer = new CompoundFileWriter();
        byte[] document = Pattern(200_000);
        byte[] table = Pattern(60_000);

        writer.Add("WordDocument", document);
        writer.Add("1Table", table);

        CompoundFile reopened = CompoundFile.Open(writer.Build());

        Assert.Equal(document, reopened.ReadStream("WordDocument"));
        Assert.Equal(table, reopened.ReadStream("1Table"));
    }

    [Fact]
    public void MixedSizes_KeepEveryStreamIntact()
    {
        var writer = new CompoundFileWriter();
        byte[] document = Pattern(9_000);
        byte[] table = Pattern(120);
        byte[] data = Pattern(70_000);

        writer.Add("WordDocument", document);
        writer.Add("1Table", table);
        writer.Add("Data", data);

        CompoundFile reopened = CompoundFile.Open(writer.Build());

        Assert.Equal(document, reopened.ReadStream("WordDocument"));
        Assert.Equal(table, reopened.ReadStream("1Table"));
        Assert.Equal(data, reopened.ReadStream("Data"));
        Assert.Equal(
            ["1Table", "Data", "WordDocument"],
            reopened.StreamNames.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void AFileBigEnoughToNeedDifatSectors_StillReadsBack()
    {
        // Beyond 109 allocation-table sectors the table itself has to be chained, which is
        // the part of the format a writer is most likely to get wrong.
        var writer = new CompoundFileWriter();
        byte[] document = Pattern(9_000_000);
        writer.Add("WordDocument", document);
        writer.Add("1Table", Pattern(64));

        CompoundFile reopened = CompoundFile.Open(writer.Build());

        Assert.Equal(document, reopened.ReadStream("WordDocument"));
        Assert.Equal(Pattern(64), reopened.ReadStream("1Table"));
    }

    [Fact]
    public void AnEmptyStream_IsStillListed()
    {
        var writer = new CompoundFileWriter();
        writer.Add("WordDocument", Pattern(700));
        writer.Add("Data", []);

        CompoundFile reopened = CompoundFile.Open(writer.Build());

        Assert.Empty(reopened.ReadStream("Data")!);
    }

    [Fact]
    public void TheContainerIsAWholeNumberOfSectorsAndCarriesTheSignature()
    {
        var writer = new CompoundFileWriter();
        writer.Add("WordDocument", Pattern(1000));
        byte[] file = writer.Build();

        Assert.Equal(0, file.Length % 512);
        Assert.Equal<byte[]>([0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1], file[..8]);
        Assert.Equal(3, file[26]);
        Assert.Equal(9, file[30]);
        Assert.Equal(6, file[32]);
    }

    private static byte[] Pattern(int length)
    {
        var bytes = new byte[length];
        for (int i = 0; i < length; i++)
            bytes[i] = (byte)((i * 31) + (i / 251));
        return bytes;
    }
}
