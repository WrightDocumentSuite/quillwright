using System.Buffers.Binary;
using System.Text;
using Quillwright.Doc.Writing;
using Quillwright.IO;
using Quillwright.Model;

namespace Quillwright.Doc.Tests;

/// <summary>
/// The streams that describe an embedded object ([MS-OLEDS] 2.3), and the pool a legacy
/// document keeps its objects in ([MS-DOC] 2.1.4).
/// </summary>
public class EmbeddedObjectTests
{
    private const string CompObjStream = "\u0001CompObj";
    private const string NativeStream = "\u0001Ole10Native";
    private const string OleStream = "\u0001Ole";

    [Fact]
    public void AnObject_NamesItselfFromItsCompObjStream()
    {
        var container = new CompoundFileWriter();
        container.Add(OleStream, new byte[20]);
        container.Add(CompObjStream, CompObj("Microsoft Excel Worksheet", "Excel.Sheet.12"));

        EmbeddedObject embedded = Wrap(container.Build());

        Assert.Equal("Microsoft Excel Worksheet", embedded.DisplayName);
        Assert.Equal("Excel.Sheet.12", embedded.ProgramId);
        Assert.False(embedded.IsLinked);
    }

    /// <summary>The flag that tells a link from an embedding is the first bit of <c>\1Ole</c>.</summary>
    [Fact]
    public void AnObjectMarkedAsALink_ReadsAsOne()
    {
        var link = new byte[20];
        BinaryPrimitives.WriteUInt32LittleEndian(link.AsSpan(4), 1);

        var container = new CompoundFileWriter();
        container.Add(OleStream, link);

        Assert.True(Wrap(container.Build()).IsLinked);
    }

    /// <summary>
    /// An object that wraps a plain file rather than being a live one is how an attachment
    /// travels in a document, and getting the file back out is the point of reading it.
    /// </summary>
    [Fact]
    public void AnObjectWrappingAFile_GivesTheFileBack()
    {
        byte[] payload = Encoding.UTF8.GetBytes("the attached text");
        var container = new CompoundFileWriter();
        container.Add(OleStream, new byte[20]);
        container.Add(NativeStream, Package("report.txt", @"C:\tmp\report.txt", payload));

        EmbeddedObject embedded = Wrap(container.Build());

        Assert.True(embedded.IsPackagedFile);
        Assert.Equal("report.txt", embedded.PackagedFileName);
        Assert.Equal(payload, embedded.PackagedFile.ToArray());
    }

    [Fact]
    public void DataOfSomeOtherShape_YieldsNoFileRatherThanNonsense()
    {
        var container = new CompoundFileWriter();
        container.Add(OleStream, new byte[20]);
        container.Add(NativeStream, [0xFF, 0xFF, 0xFF, 0x7F, 0x02, 0x00, 0x41, 0x00]);

        EmbeddedObject embedded = Wrap(container.Build());

        Assert.False(embedded.IsPackagedFile);
        Assert.Null(embedded.PackagedFileName);
    }

    /// <summary>
    /// The pool is what a legacy document keeps its objects in, and the corpus is what proves
    /// the storage is reached from the field separator that names it.
    /// </summary>
    [Fact]
    public void EmbeddedObjects_AreFoundInTheCorpus()
    {
        string root = ReferenceCorpus.Telerik;
        Assert.SkipUnless(Directory.Exists(root), ReferenceCorpus.Absent);

        List<EmbeddedObject> found = [];
        foreach (string path in Directory.EnumerateFiles(root, "*.doc", SearchOption.AllDirectories))
        {
            if (new FileInfo(path).Length is not (> 0 and < 8 * 1024 * 1024))
                continue;

            try
            {
                found.AddRange(DocReader.Load(File.ReadAllBytes(path)).EmbeddedObjects);
            }
            catch (Exception error) when (RefusedByDesign.Matches(error))
            {
                // Encrypted and pre-Word 97 files are refused rather than read.
            }
        }

        Assert.NotEmpty(found);
        Assert.All(found, static o => Assert.NotEmpty(o.Content.ToArray()));
        Assert.All(found, static o => Assert.StartsWith("ObjectPool/_", o.Location, StringComparison.Ordinal));
    }

    /// <summary>Reads a synthesized storage the way the reader reads one out of a package.</summary>
    private static EmbeddedObject Wrap(byte[] content)
    {
        OleDescription description = Assert.IsType<OleDescription>(OleContainer.Describe(content));
        return new EmbeddedObject
        {
            Location = "test",
            ProgramId = description.ProgramId,
            DisplayName = description.DisplayName,
            IsLinked = description.IsLinked,
            Content = content,
            PackagedFileName = description.PackagedFileName,
            PackagedFile = description.PackagedFile ?? ReadOnlyMemory<byte>.Empty,
        };
    }

    /// <summary>Builds a <c>\1CompObj</c> stream ([MS-OLEDS] 2.3.8).</summary>
    private static byte[] CompObj(string userType, string programId)
    {
        var bytes = new List<byte>(new byte[28]);
        AnsiString(bytes, userType);
        bytes.AddRange(new byte[4]);
        AnsiString(bytes, programId);
        bytes.AddRange([0xF4, 0x39, 0xB2, 0x71]);
        UnicodeString(bytes, userType);
        return [.. bytes];
    }

    /// <summary>Builds the native data of a packaged file, as the packager lays it out.</summary>
    private static byte[] Package(string label, string original, byte[] payload)
    {
        var body = new List<byte> { 0x02, 0x00 };
        body.AddRange(Encoding.Latin1.GetBytes(label));
        body.Add(0);
        body.AddRange(Encoding.Latin1.GetBytes(original));
        body.Add(0);
        body.AddRange(new byte[4]);

        byte[] temporary = Encoding.Latin1.GetBytes(original + "\0");
        body.AddRange(BitConverter.GetBytes(temporary.Length));
        body.AddRange(temporary);
        body.AddRange(BitConverter.GetBytes(payload.Length));
        body.AddRange(payload);

        return [.. BitConverter.GetBytes(body.Count), .. body];
    }

    private static void AnsiString(List<byte> bytes, string text)
    {
        byte[] encoded = Encoding.Latin1.GetBytes(text + "\0");
        bytes.AddRange(BitConverter.GetBytes(encoded.Length));
        bytes.AddRange(encoded);
    }

    private static void UnicodeString(List<byte> bytes, string text)
    {
        bytes.AddRange(BitConverter.GetBytes(text.Length + 1));
        bytes.AddRange(Encoding.Unicode.GetBytes(text + "\0"));
    }
}
