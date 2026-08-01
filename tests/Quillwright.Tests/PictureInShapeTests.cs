using System.IO.Compression;
using System.Text;
using Quillwright.Model;
using Quillwright.Primitives;

namespace Quillwright.Tests;

/// <summary>
/// A picture Word writes as the fill of a shape rather than as a picture of its own
/// (ISO/IEC 29500-1 §20.1.8.14, <c>a:blipFill</c>, inside a <c>wps:wsp</c>).
/// </summary>
/// <remarks>
/// The two arrangements draw the same thing, so a reader that only knows the first leaves
/// half the images in a document invisible to the media API and unresizable. A shape that
/// also carries text is a text box and stays one.
/// </remarks>
public class PictureInShapeTests
{
    [Fact]
    public async Task APictureFillingAShape_IsReadAsAPicture()
    {
        using MemoryStream package = await ShapeAsync(withText: false);

        WordDocument document = await LoadAsync(package);

        Picture picture = Objects(document).OfType<Picture>().Single();
        Assert.Equal("image/png", picture.Image.ContentType);
        Assert.Equal(Length.FromCentimeters(3).Emu, picture.Width.Emu);
    }

    [Fact]
    public async Task APictureFillingAShape_LeftAlone_IsSavedByteForByte()
    {
        using MemoryStream package = await ShapeAsync(withText: false);
        using MemoryStream once = await DocumentFixture.SaveAsync(await LoadAsync(package));

        once.Position = 0;
        using MemoryStream twice = await DocumentFixture.SaveAsync(await LoadAsync(once));

        Assert.Contains(":wsp", Drawing(ReadDocumentPart(once)), StringComparison.Ordinal);
        Assert.Equal(Drawing(ReadDocumentPart(once)), Drawing(ReadDocumentPart(twice)));
    }

    [Fact]
    public async Task ResizingAPictureFillingAShape_KeepsTheShapeAroundIt()
    {
        using MemoryStream package = await ShapeAsync(withText: false);
        WordDocument document = await LoadAsync(package);

        Picture picture = Objects(document).OfType<Picture>().Single();
        picture.Width = Length.FromCentimeters(6);

        using MemoryStream saved = await DocumentFixture.SaveAsync(document);
        string drawing = Drawing(ReadDocumentPart(saved));

        Assert.Contains(":wsp", drawing, StringComparison.Ordinal);
        Assert.Contains($"cx=\"{Length.FromCentimeters(6).Emu}\"", drawing, StringComparison.Ordinal);
    }

    /// <summary>A shape that holds words is a text box, whatever it is filled with.</summary>
    [Fact]
    public async Task AShapeThatAlsoHoldsText_StaysATextBox()
    {
        using MemoryStream package = await ShapeAsync(withText: true);

        WordDocument document = await LoadAsync(package);

        Assert.Empty(Objects(document).OfType<Picture>());
        Assert.Contains("inside the shape", document.GetText(), StringComparison.Ordinal);
    }

    private static ValueTask<WordDocument> LoadAsync(Stream package) =>
        WordDocument.LoadAsync(package, cancellationToken: TestContext.Current.CancellationToken);

    private static IEnumerable<InlineObject> Objects(WordDocument document) =>
        document.AllContainers
            .SelectMany(static container => container.Blocks.Paragraphs)
            .SelectMany(static paragraph => paragraph.Objects)
            .Select(static anchored => anchored.Object);

    private static string Drawing(string documentXml)
    {
        int start = documentXml.IndexOf("<w:drawing", StringComparison.Ordinal);
        int end = documentXml.IndexOf("</w:drawing>", StringComparison.Ordinal) + "</w:drawing>".Length;
        Assert.InRange(start, 0, end);
        return documentXml[start..end];
    }

    /// <summary>
    /// A package holding one picture written the way Word writes a picture-filled shape: the
    /// image is the shape's fill rather than a <c>pic:pic</c> of its own.
    /// </summary>
    private static async Task<MemoryStream> ShapeAsync(bool withText)
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph().AppendPicture(
            ImageData.FromBytes(TestImages.Png), Length.FromCentimeters(3), Length.FromCentimeters(2));

        using MemoryStream plain = await DocumentFixture.SaveAsync(document);
        return Rewrite(plain, xml => Reshape(xml, withText));
    }

    private static string Reshape(string documentXml, bool withText)
    {
        int start = documentXml.IndexOf("<pic:pic", StringComparison.Ordinal);
        int end = documentXml.IndexOf("</pic:pic>", StringComparison.Ordinal) + "</pic:pic>".Length;
        Assert.InRange(start, 0, end);

        string embed = documentXml[start..end];
        int blip = embed.IndexOf("<a:blip", StringComparison.Ordinal);
        string relationship = embed[blip..(embed.IndexOf("/>", blip, StringComparison.Ordinal) + 2)];

        string text = withText
            ? "<wps:txbx><w:txbxContent><w:p><w:r><w:t>inside the shape</w:t></w:r></w:p></w:txbxContent></wps:txbx>"
            : string.Empty;

        string shape =
            "<wps:wsp><wps:cNvSpPr/><wps:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/>" +
            $"<a:ext cx=\"{Length.FromCentimeters(3).Emu}\" cy=\"{Length.FromCentimeters(2).Emu}\"/></a:xfrm>" +
            "<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom>" +
            $"<a:blipFill>{relationship}<a:stretch><a:fillRect/></a:stretch></a:blipFill></wps:spPr>" +
            $"{text}<wps:bodyPr/></wps:wsp>";

        return documentXml[..start] + shape + documentXml[end..];
    }

    private static string ReadDocumentPart(MemoryStream package)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        using var reader = new StreamReader(archive.GetEntry("word/document.xml")!.Open());
        return reader.ReadToEnd();
    }

    /// <summary>Copies a package through, replacing the markup of the main document part.</summary>
    private static MemoryStream Rewrite(MemoryStream package, Func<string, string> edit)
    {
        var result = new MemoryStream();
        package.Position = 0;
        using (var source = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true))
        using (var target = new ZipArchive(result, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (ZipArchiveEntry entry in source.Entries)
            {
                using Stream from = entry.Open();
                using Stream to = target.CreateEntry(entry.FullName).Open();
                if (entry.FullName != "word/document.xml")
                {
                    from.CopyTo(to);
                    continue;
                }

                using var reader = new StreamReader(from);
                byte[] edited = Encoding.UTF8.GetBytes(edit(reader.ReadToEnd()));
                to.Write(edited, 0, edited.Length);
            }
        }

        result.Position = 0;
        return result;
    }
}
