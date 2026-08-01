using System.IO.Compression;
using System.Text;
using Quillwright.Model;
using Quillwright.Primitives;

namespace Quillwright.Tests;

/// <summary>
/// Resizing a picture that came out of a file. The model carries a size and a name and little
/// else, so rebuilding the drawing from it would throw away everything else the file said.
/// </summary>
public class PictureResizeTests
{
    [Fact]
    public async Task ResizingAPicture_ChangesTheSizeInThePreservedMarkup()
    {
        WordDocument document = await LoadAsync(await InlinePictureAsync());
        Picture picture = Pictures(document).Single();
        picture.Width = Length.FromCentimeters(7);
        picture.Height = Length.FromCentimeters(5);

        using MemoryStream saved = await DocumentFixture.SaveAsync(document);
        WordDocument reloaded = await LoadAsync(saved);

        Assert.Equal(Length.FromCentimeters(7).Emu, Pictures(reloaded).Single().Width.Emu);
        Assert.Equal(Length.FromCentimeters(5).Emu, Pictures(reloaded).Single().Height.Emu);
    }

    /// <summary>
    /// The point of rewriting rather than regenerating: a picture that floats has an anchor,
    /// and a generated drawing has none.
    /// </summary>
    [Fact]
    public async Task ResizingAFloatingPicture_LeavesItFloating()
    {
        using MemoryStream package = await FloatingPictureAsync();
        WordDocument document = await LoadAsync(package);
        Picture picture = Pictures(document).Single();

        Assert.False(picture.IsInline);
        picture.Width = Length.FromCentimeters(7);

        using MemoryStream saved = await DocumentFixture.SaveAsync(document);
        string part = OpenXmlAssert.ReadPart(saved, "word/document.xml");

        Assert.Contains("<wp:anchor", part, StringComparison.Ordinal);
        Assert.Contains("<wp:wrapSquare", part, StringComparison.Ordinal);
        Assert.DoesNotContain("<wp:inline", part, StringComparison.Ordinal);
        Assert.False(Pictures(await LoadAsync(saved)).Single().IsInline);
        OpenXmlAssert.Valid(saved, "a resized floating picture");
    }

    [Fact]
    public async Task RenamingAPicture_ChangesOnlyItsName()
    {
        using MemoryStream package = await FloatingPictureAsync();
        WordDocument document = await LoadAsync(package);
        Pictures(document).Single().Description = "A chart of results";

        using MemoryStream saved = await DocumentFixture.SaveAsync(document);
        string part = OpenXmlAssert.ReadPart(saved, "word/document.xml");

        Assert.Contains("descr=\"A chart of results\"", part, StringComparison.Ordinal);
        Assert.Contains("<wp:wrapSquare", part, StringComparison.Ordinal);
    }

    private static IEnumerable<Picture> Pictures(WordDocument document) =>
        document.Paragraphs.SelectMany(static p => p.Objects).Select(static o => o.Object).OfType<Picture>();

    private static ValueTask<WordDocument> LoadAsync(Stream package) =>
        WordDocument.LoadAsync(package, cancellationToken: TestContext.Current.CancellationToken);

    private static async Task<MemoryStream> InlinePictureAsync()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph().AppendPicture(
            ImageData.FromBytes(TestImages.Png), Length.FromCentimeters(3), Length.FromCentimeters(2));
        return await DocumentFixture.SaveAsync(document);
    }

    /// <summary>
    /// A package whose picture is anchored and wrapped, built by turning the inline drawing
    /// this library generates into the anchored one Word writes.
    /// </summary>
    private static async Task<MemoryStream> FloatingPictureAsync()
    {
        using MemoryStream plain = await InlinePictureAsync();
        return Rewrite(plain, static xml => xml
            .Replace(
                "<wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\">",
                "<wp:anchor distT=\"0\" distB=\"0\" distL=\"114300\" distR=\"114300\" simplePos=\"0\"" +
                " relativeHeight=\"251658240\" behindDoc=\"0\" locked=\"0\" layoutInCell=\"1\" allowOverlap=\"1\">" +
                "<wp:simplePos x=\"0\" y=\"0\"/>" +
                "<wp:positionH relativeFrom=\"column\"><wp:posOffset>114300</wp:posOffset></wp:positionH>" +
                "<wp:positionV relativeFrom=\"paragraph\"><wp:posOffset>228600</wp:posOffset></wp:positionV>",
                StringComparison.Ordinal)
            // The wrap goes before the name, which is where the schema wants it.
            .Replace("<wp:docPr", "<wp:wrapSquare wrapText=\"bothSides\"/><wp:docPr", StringComparison.Ordinal)
            .Replace("</wp:inline>", "</wp:anchor>", StringComparison.Ordinal));
    }

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
