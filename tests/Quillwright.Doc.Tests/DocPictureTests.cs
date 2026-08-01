using Quillwright.Diagnostics;
using Quillwright.Doc.Writing;
using Quillwright.Model;
using Quillwright.Primitives;

namespace Quillwright.Doc.Tests;

/// <summary>
/// A picture is stored nowhere near the text that shows it: the text holds a placeholder
/// whose character properties name an offset in a third stream. These tests follow that
/// indirection all the way back to the original bytes.
/// </summary>
public class DocPictureTests
{
    [Fact]
    public void APicture_SurvivesTheRoundTripByteForByte()
    {
        ImageData image = Png();
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("before ");
        paragraph.AppendPicture(image, Length.FromCentimeters(4), Length.FromCentimeters(3));
        paragraph.AppendText(" after");
        Add(document, paragraph);

        Picture reopened = Pictures(RoundTrip(document)).Single();

        Assert.Equal(image.Bytes.ToArray(), reopened.Image.Bytes.ToArray());
        Assert.Equal("image/png", reopened.Image.ContentType);
    }

    [Fact]
    public void APicturesSize_SurvivesTheRoundTrip()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph();
        paragraph.AppendPicture(Png(), Length.FromCentimeters(4), Length.FromCentimeters(3));
        Add(document, paragraph);

        Picture reopened = Pictures(RoundTrip(document)).Single();

        Assert.Equal(Length.FromCentimeters(4).Twips, reopened.Width.Twips);
        Assert.Equal(Length.FromCentimeters(3).Twips, reopened.Height.Twips);
    }

    [Fact]
    public void APicture_StaysWhereItWasInTheText()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("before ");
        paragraph.AppendPicture(Png());
        paragraph.AppendText(" after");
        Add(document, paragraph);

        Paragraph reopened = RoundTrip(document).Sections.SelectMany(static s => s.Blocks).OfType<Paragraph>().First();

        Assert.Equal(7, reopened.Objects.Single(static o => o.Object is Picture).Offset);
        Assert.Contains("before", reopened.Text, StringComparison.Ordinal);
        Assert.Contains("after", reopened.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void AJpeg_SurvivesTheRoundTrip()
    {
        ImageData image = Jpeg();
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph();
        paragraph.AppendPicture(image);
        Add(document, paragraph);

        Picture reopened = Pictures(RoundTrip(document)).Single();

        Assert.Equal(image.Bytes.ToArray(), reopened.Image.Bytes.ToArray());
        Assert.Equal("image/jpeg", reopened.Image.ContentType);
    }

    [Fact]
    public void APictureInsideACompatibilityBlock_StillConverts()
    {
        // The binary format has no mc:AlternateContent, so the branch the .docx reader
        // selected has to be unwrapped rather than dropped along with its wrapper.
        ImageData image = Png();
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("before ");
        paragraph.AppendObject(new AlternateContent(
            "<mc:AlternateContent><mc:Choice Requires=\"wps\">",
            new Picture { Image = image, Width = Length.FromCentimeters(4), Height = Length.FromCentimeters(3) },
            "</mc:Choice></mc:AlternateContent>"));
        Add(document, paragraph);
        document.Media.Add(image);

        Picture reopened = Pictures(RoundTrip(document)).Single();

        Assert.Equal(image.Bytes.ToArray(), reopened.Image.Bytes.ToArray());
        Assert.Equal(Length.FromCentimeters(4).Twips, reopened.Width.Twips);
    }

    [Fact]
    public void TheSameImageUsedTwice_IsStoredOnce()
    {
        ImageData image = Png();
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph();
        paragraph.AppendPicture(image);
        paragraph.AppendText(" and again ");
        paragraph.AppendPicture(image);
        Add(document, paragraph);

        var single = WordDocument.Create();
        var alone = new Paragraph();
        alone.AppendPicture(image);
        Add(single, alone);

        byte[] twice = DocWriter.Save(document);
        int repeated = CompoundFile.Open(twice).ReadStream("Data")!.Length;
        int once = CompoundFile.Open(DocWriter.Save(single)).ReadStream("Data")!.Length;

        Assert.Equal(2, Pictures(DocReader.Load(twice)).Count);
        Assert.Equal(once, repeated);
    }

    [Fact]
    public void PicturesCanBeTurnedOff()
    {
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("text only");
        paragraph.AppendPicture(Png());
        Add(document, paragraph);

        byte[] file = DocWriter.Save(document, new DocWriteOptions { WriteImages = false });

        Assert.Empty(Pictures(DocReader.Load(file)));
        Assert.Null(CompoundFile.Open(file).ReadStream("Data"));
    }

    [Fact]
    public void AnUnsupportedImageFormat_RaisesAWarningRatherThanFailing()
    {
        var warnings = new List<DocumentWarning>();
        WordDocument document = WordDocument.Create();
        var paragraph = new Paragraph("with a picture");
        paragraph.AppendPicture(ImageData.FromBytes(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 1, 0, 1, 0 }, "image/gif"));
        Add(document, paragraph);

        WordDocument reopened = DocReader.Load(DocWriter.Save(document, new DocWriteOptions { OnWarning = warnings.Add }));

        Assert.Contains(warnings, w => w.Code == WarningCode.UnresolvedMedia);
        Assert.Empty(Pictures(reopened));
        Assert.Contains("with a picture", reopened.Sections[0].GetText(), StringComparison.Ordinal);
    }

    private static WordDocument RoundTrip(WordDocument document) => DocReader.Load(DocWriter.Save(document));

    private static List<Picture> Pictures(WordDocument document) =>
    [
        .. document.Sections.SelectMany(static s => s.Blocks)
            .OfType<Paragraph>()
            .SelectMany(static p => p.Objects)
            .Select(static o => o.Object)
            .OfType<Picture>(),
    ];

    private static void Add(WordDocument document, Block block) => document.Sections[0].Blocks.Add(block);

    /// <summary>The smallest valid PNG: a single opaque pixel.</summary>
    private static ImageData Png() => ImageData.FromBytes(Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg=="));

    /// <summary>The smallest valid JPEG this test needs: a one-pixel grey image.</summary>
    private static ImageData Jpeg() => ImageData.FromBytes(Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/wAALCAABAAEBAREA/8QAFAABAAAAAAAAAAAAAAAAAAAACf/EABQQAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQEAAD8AKp//2Q=="));
}
