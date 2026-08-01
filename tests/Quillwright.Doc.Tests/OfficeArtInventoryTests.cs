using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace Quillwright.Doc.Tests;

/// <summary>
/// What the drawing layer of the reference corpus actually contains.
/// </summary>
/// <remarks>
/// <para>
/// [MS-ODRAW] describes several hundred records and over a thousand shape properties, and gives
/// no indication of which of them Word writes. Designing against its table of contents would
/// mean building for a format nobody produces, so the design is measured against the corpus
/// instead: these tests count what is there, and the counts are what the reader is built to.
/// </para>
/// <para>
/// They are assertions rather than a report because a count that changes is a corpus that
/// changed, and a reader built on the old numbers should be told.
/// </para>
/// </remarks>
public class OfficeArtInventoryTests
{
    private static readonly string CorpusRoot = ReferenceCorpus.Telerik;

    /// <summary>Records worth counting, by the number [MS-ODRAW] gives them.</summary>
    private static readonly Dictionary<ushort, string> Records = new()
    {
        [0xF003] = "OfficeArtSpgrContainer",
        [0xF004] = "OfficeArtSpContainer",
        [0xF008] = "OfficeArtFDG",
        [0xF009] = "OfficeArtFSPGR",
        [0xF00A] = "OfficeArtFSP",
        [0xF00B] = "OfficeArtFOPT",
        [0xF00D] = "OfficeArtClientTextbox",
        [0xF00E] = "OfficeArtClientAnchor",
        [0xF00F] = "OfficeArtClientData",
        [0xF010] = "OfficeArtClientAnchorWord",
        [0xF011] = "OfficeArtClientTextboxWord",
        [0xF121] = "OfficeArtSecondaryFOPT",
        [0xF122] = "OfficeArtTertiaryFOPT",
        [0xF11D] = "OfficeArtFDGGBlock",
        [0xF11E] = "OfficeArtColorMRUContainer",
        [0xF11F] = "OfficeArtFOPTE",
    };

    /// <summary>
    /// The shape properties a renderer would want, by identifier ([MS-ODRAW] 2.3). Everything
    /// else is counted together, because what matters is whether these are there.
    /// </summary>
    private static readonly Dictionary<int, string> Properties = new()
    {
        [0x0004] = "rotation",
        [0x0080] = "lTxid",
        [0x0104] = "pib",
        [0x0140] = "geoLeft",
        [0x0141] = "geoTop",
        [0x0142] = "geoRight",
        [0x0143] = "geoBottom",
        [0x0144] = "shapePath",
        [0x0145] = "pVertices",
        [0x0146] = "pSegmentInfo",
        [0x0147] = "adjustValue",
        [0x0180] = "fillType",
        [0x0181] = "fillColor",
        [0x0183] = "fillBackColor",
        [0x01BF] = "fillStyleBooleans",
        [0x01C0] = "lineColor",
        [0x01CB] = "lineWidth",
        [0x01CD] = "lineDashing",
        [0x01FF] = "lineStyleBooleans",
        [0x0200] = "shadowType",
        [0x0201] = "shadowColor",
        [0x023F] = "shadowStyleBooleans",
        [0x038F] = "posh",
        [0x0390] = "posrelh",
        [0x0391] = "posv",
        [0x0392] = "posrelv",
        [0x03BF] = "groupShapeBooleans",
    };

    /// <summary>The whole corpus, counted once.</summary>
    private static readonly Lazy<Inventory> Corpus = new(Scan);

    [Fact]
    public void TheCorpus_HoldsEnoughDrawingsToDesignAgainst()
    {
        Inventory found = Corpus.Value;
        Assert.SkipWhen(found.Documents == 0, ReferenceCorpus.Absent);

        Assert.True(found.Documents >= 50, $"only {found.Documents} documents were scanned");
        Assert.True(found.Shapes >= 100, $"only {found.Shapes} shapes were found");
    }

    /// <summary>
    /// The finding that decided the order of the work: every group in the corpus is the one a
    /// story is wrapped in, so there is no group inside a group and no coordinate transform to
    /// apply to anything. Building for group transforms would have been building for a file
    /// nobody writes.
    /// </summary>
    [Fact]
    public void EveryGroup_IsTheOneEachStoryIsWrappedIn()
    {
        Inventory found = Corpus.Value;
        Assert.SkipWhen(found.Documents == 0, ReferenceCorpus.Absent);

        Assert.Equal(found.Stories, found.Record(0xF003));
        Assert.Equal(found.Stories, found.Record(0xF009));
        Assert.Equal(found.Stories, found.ShapeTypes.GetValueOrDefault(0));
    }

    /// <summary>
    /// The other finding: no shape in the corpus draws a path of its own, so custom geometry
    /// — the vertices, the segment list, the geometry bounds — has nothing to read.
    /// </summary>
    [Fact]
    public void NoShapeInTheCorpus_DrawsAPathOfItsOwn()
    {
        Inventory found = Corpus.Value;
        Assert.SkipWhen(found.Documents == 0, ReferenceCorpus.Absent);

        Assert.Equal(0, found.Property(0x0144));
        Assert.Equal(0, found.Property(0x0145));
        Assert.Equal(0, found.Property(0x0146));
        Assert.Equal(0, found.Property(0x0200));
    }

    /// <summary>
    /// What is there instead, and what the reader was therefore built to: rectangles, picture
    /// frames and lettering, painted with a fill, a line and a rotation.
    /// </summary>
    [Fact]
    public void WhatTheCorpusDrawsIsRectanglesPicturesAndLettering()
    {
        Inventory found = Corpus.Value;
        Assert.SkipWhen(found.Documents == 0, ReferenceCorpus.Absent);

        int drawn = found.Shapes - found.ShapeTypes.GetValueOrDefault(0);
        int known = found.ShapeTypes
            .Where(static entry => entry.Key is 1 or 75 or 202 || entry.Key is >= 136 and <= 201)
            .Sum(static entry => entry.Value);

        Assert.Equal(drawn, known);
        Assert.True(found.Property(0x01BF) > 0, "no shape states whether it is filled");
        Assert.True(found.Property(0x01FF) > 0, "no shape states whether it has a line");
        Assert.True(found.Property(0x00C0) > 0, "no shape carries lettering");
    }

    /// <summary>The inventory itself, written where a person can read it.</summary>
    [Fact]
    public void TheInventory_IsWorthWritingDown()
    {
        Inventory found = Corpus.Value;
        Assert.SkipWhen(found.Documents == 0, ReferenceCorpus.Absent);

        TestContext.Current.TestOutputHelper?.WriteLine(found.ToString());
        Assert.NotEmpty(found.ToString());
    }

    private static Inventory Scan()
    {
        var found = new Inventory();
        if (!Directory.Exists(CorpusRoot))
            return found;

        foreach (string path in Directory.EnumerateFiles(CorpusRoot, "*.doc", SearchOption.AllDirectories))
        {
            if (new FileInfo(path).Length is <= 0 or > 8 * 1024 * 1024)
                continue;

            try
            {
                Count(path, found);
            }
            catch (Exception error) when (RefusedByDesign.Matches(error))
            {
                // A file the reader declines is not one the inventory can say anything about.
            }
        }

        return found;
    }

    /// <summary>
    /// Opens the two streams the drawings live between, without converting the document: the
    /// inventory is about what is in the file, not about what the reader makes of it.
    /// </summary>
    private static void Count(string path, Inventory found)
    {
        byte[] bytes = File.ReadAllBytes(path);
        if (!Quillwright.IO.CompoundFile.IsCompoundFile(bytes))
            return;

        Quillwright.IO.CompoundFile container = Quillwright.IO.CompoundFile.Open(bytes);
        if (container.ReadStream("WordDocument") is not { } document)
            return;

        bool table1 = FileInformationBlock.PrefersTable1(document);
        if ((container.ReadStream(table1 ? "1Table" : "0Table") ?? container.ReadStream(table1 ? "0Table" : "1Table")) is not { } table)
            return;

        (int offset, int length) = FileInformationBlock.Read(document).Drawings;
        if (length <= 0 || offset < 0 || offset + length > table.Length)
            return;

        // The region opens with the document-wide records and then holds one drawing per
        // story, each headed by a single byte saying which story it belongs to ([MS-DOC]
        // 2.9.172) — so the stories cannot be reached by walking siblings.
        int end = offset + length;
        if (OfficeArtRecord.Find(table, offset, end, 0xF000) is not { } group)
            return;

        found.Documents++;
        Walk(table, offset, group.End, found);

        int position = group.End;
        while (OfficeArtRecord.TryRead(table, position + 1, end, out OfficeArtRecord drawing) && drawing.Type == 0xF002)
        {
            found.Stories++;
            Walk(table, drawing.Body, drawing.End, found);
            position = drawing.End;
        }
    }

    private static void Walk(byte[] table, int start, int end, Inventory found)
    {
        foreach (OfficeArtRecord record in OfficeArtRecord.Walk(table, start, end))
        {
            found.Records[record.Type] = found.Record(record.Type) + 1;

            switch (record.Type)
            {
                case 0xF00A when record.Length >= 8:
                    // The shape's preset geometry is the instance field of its own record
                    // header ([MS-ODRAW] 2.2.40), not a field of the body.
                    found.Shapes++;
                    found.ShapeTypes[record.Instance] = found.ShapeTypes.GetValueOrDefault(record.Instance) + 1;
                    break;

                case 0xF00B or 0xF121 or 0xF122:
                    CountProperties(table, record, found);
                    break;
            }

            if (record.IsContainer)
                Walk(table, record.Body, record.End, found);
        }
    }

    private static void CountProperties(byte[] table, OfficeArtRecord options, Inventory found)
    {
        for (int i = 0; i < options.Instance; i++)
        {
            int at = options.Body + (i * 6);
            if (at + 6 > options.End)
                return;

            int identifier = BinaryPrimitives.ReadUInt16LittleEndian(table.AsSpan(at)) & 0x3FFF;
            found.Properties[identifier] = found.Property(identifier) + 1;
        }
    }

    /// <summary>What one sweep of the corpus counted.</summary>
    private sealed class Inventory
    {
        public int Documents { get; set; }

        public int Stories { get; set; }

        public int Shapes { get; set; }

        public Dictionary<ushort, int> Records { get; } = [];

        public Dictionary<int, int> Properties { get; } = [];

        public Dictionary<int, int> ShapeTypes { get; } = [];

        public int Record(ushort type) => Records.GetValueOrDefault(type);

        public int Property(int identifier) => Properties.GetValueOrDefault(identifier);

        public override string ToString()
        {
            var text = new StringBuilder()
                .Append(Documents).Append(" documents, ").Append(Stories).Append(" stories, ")
                .Append(Shapes).AppendLine(" shapes")
                .AppendLine("Records:");

            foreach ((ushort type, int count) in Records.OrderByDescending(static entry => entry.Value))
            {
                text.Append("  ").Append(OfficeArtInventoryTests.Records.GetValueOrDefault(type, "0x" + type.ToString("X4", CultureInfo.InvariantCulture)))
                    .Append(' ').Append(count).AppendLine();
            }

            text.AppendLine("Shape types:");
            foreach ((int type, int count) in ShapeTypes.OrderByDescending(static entry => entry.Value).Take(20))
                text.Append("  ").Append(type).Append(' ').Append(count).AppendLine();

            text.AppendLine("Properties:");
            foreach ((int identifier, int count) in Properties.OrderByDescending(static entry => entry.Value).Take(40))
            {
                text.Append("  0x").Append(identifier.ToString("X4", CultureInfo.InvariantCulture)).Append(' ')
                    .Append(OfficeArtInventoryTests.Properties.GetValueOrDefault(identifier, "?")).Append(' ')
                    .Append(count).AppendLine();
            }

            return text.ToString();
        }
    }
}
