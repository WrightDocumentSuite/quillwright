using Quillwright.Model;

namespace Quillwright.Doc.Tests;

/// <summary>
/// Floating drawings: a picture that does not sit in the line of text is stored nowhere near
/// it. The text has an anchor, the anchor names a shape, the shape names a place in a store
/// the whole document shares, and only there are there any image bytes.
/// </summary>
public class DocDrawingTests
{
    private static readonly string CorpusRoot =
        ReferenceCorpus.TelerikPath("Flow/Tests/FormatProviders/Doc/TestDocuments/Doc");

    private const string ImageWatermark = @"Watermarks\WordImageWatermark.doc";
    private const string MultipleWatermarks = @"Watermarks\WordMultipleWatermarks.doc";
    private const string TextWatermark = @"Watermarks\WordTextWatermark.doc";

    [Fact]
    public async Task AFloatingPicture_ArrivesAsAPicture()
    {
        WordDocument document = await LoadAsync(ImageWatermark);

        List<Picture> pictures = Pictures(document);

        Assert.NotEmpty(pictures);
        Assert.All(pictures, static picture =>
        {
            Assert.False(picture.IsInline);
            Assert.Equal("image/png", picture.Image.ContentType);
        });
    }

    [Fact]
    public async Task AFloatingPicture_IsSizedFromItsAnchorsRectangle()
    {
        WordDocument document = await LoadAsync(ImageWatermark);

        Picture picture = Pictures(document)[0];

        Assert.True(picture.Width.Twips > 0, "the picture came back with no width");
        Assert.True(picture.Height.Twips > 0, "the picture came back with no height");
    }

    /// <summary>
    /// The image lives in the document's store rather than in the shape, so two shapes that
    /// show the same picture have to arrive as two pictures of one image, not two images.
    /// </summary>
    [Fact]
    public async Task TwoShapesShowingOneImage_ShareIt()
    {
        WordDocument document = await LoadAsync(MultipleWatermarks);

        List<Picture> pictures = Pictures(document);

        Assert.True(pictures.Count >= 2, $"expected several watermarks, found {pictures.Count}");
        Assert.Single(pictures.Select(static p => p.Image).Distinct());
        Assert.Single(document.Media);
    }

    [Fact]
    public async Task AFloatingPicturesImage_IsRegisteredWithTheDocument()
    {
        WordDocument document = await LoadAsync(ImageWatermark);

        Assert.Same(Pictures(document)[0].Image, document.Media.Single());
    }

    /// <summary>
    /// A text watermark is WordArt, and WordArt keeps its words in a property of the shape
    /// rather than in the text of the document. A reader that skips the shape loses them
    /// completely, so they come across as ordinary text in a box — and the fact that the
    /// lettering they were drawn with did not is said in the diagnostics.
    /// </summary>
    [Fact]
    public async Task ATextWatermark_KeepsItsWordsAndSaysTheLetteringWasLost()
    {
        WordDocument document = await LoadAsync(TextWatermark);

        Assert.Empty(Pictures(document));

        List<Model.TextBox> lettering = [.. document.AllContainers.OfType<Model.TextBox>()];
        Assert.NotEmpty(lettering);
        Assert.All(lettering, static box => Assert.NotEmpty(box.GetText().Trim()));

        Assert.Contains(
            document.LoadDiagnostics,
            static warning => warning.Message.Contains("WordArt", StringComparison.Ordinal));
    }

    /// <summary>
    /// A watermark sits behind the text and the text takes no notice of it, which is exactly
    /// the combination the flag word at the end of the anchor exists to say ([MS-DOC] 2.9.253).
    /// </summary>
    [Fact]
    public async Task AWatermark_ComesBackBehindTheTextAndOutOfItsWay()
    {
        WordDocument document = await LoadAsync(ImageWatermark);

        PictureAnchor anchor = Assert.IsType<PictureAnchor>(Pictures(document)[0].Anchor);

        Assert.Equal(TextWrapping.None, anchor.Wrapping);
        Assert.True(anchor.BehindText, "a watermark that is not behind the text would hide it");
    }

    /// <summary>
    /// The origin a watermark is measured from, which is the margin box rather than the page.
    /// The two are different rectangles, so a picture placed against the wrong one lands
    /// wherever the margins happen to be wide.
    /// </summary>
    [Fact]
    public async Task AFloatingPicture_KeepsWhereItIsMeasuredFrom()
    {
        WordDocument document = await LoadAsync(ImageWatermark);

        PictureAnchor anchor = Pictures(document)[0].Anchor!;

        Assert.Equal(AnchorOrigin.Margin, anchor.HorizontalFrom);
        Assert.Equal(AnchorOrigin.Margin, anchor.VerticalFrom);
        Assert.Equal(AnchorAlignment.Offset, anchor.HorizontalAlignment);
    }

    /// <summary>The position has to survive being written out as well as being read.</summary>
    [Fact]
    public async Task AWatermarksPosition_SurvivesConversionToAPackage()
    {
        WordDocument document = await LoadAsync(ImageWatermark);

        var saved = new MemoryStream();
        await document.SaveAsync(saved, cancellationToken: TestContext.Current.CancellationToken);
        string markup = Package(saved, "word/header1.xml") ?? Package(saved, "word/document.xml")!;

        Assert.Contains("behindDoc=\"1\"", markup, StringComparison.Ordinal);
        Assert.Contains("<wp:wrapNone/>", markup, StringComparison.Ordinal);
        Assert.Contains("<wp:positionH relativeFrom=\"margin\">", markup, StringComparison.Ordinal);
        Assert.Contains("<wp:positionV relativeFrom=\"margin\">", markup, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every floating picture in the corpus, because the flag word is one field read out of
    /// many and a reader that has the bits wrong produces plausible nonsense rather than
    /// nothing.
    /// </summary>
    [Fact]
    public async Task AcrossTheCorpus_EveryFloatingPictureIsPlacedSomewhereReal()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        List<PictureAnchor> anchors = [];

        foreach (string path in ReferenceCorpus.FilesUnder(CorpusRoot, "*.doc"))
        {
            try
            {
                anchors.AddRange(Pictures(await DocReader.LoadAsync(path, cancellationToken))
                    .Where(static picture => !picture.IsInline)
                    .Select(static picture => picture.Anchor)
                    .OfType<PictureAnchor>());
            }
            catch (Exception error) when (RefusedByDesign.Matches(error))
            {
                // Encrypted and pre-Word 97 files are refused by design.
            }
        }

        Assert.SkipWhen(anchors.Count == 0, ReferenceCorpus.Absent);
        Assert.All(anchors, static anchor =>
        {
            Assert.True(Enum.IsDefined(anchor.Wrapping), $"unknown wrapping {(int)anchor.Wrapping}");
            Assert.True(Enum.IsDefined(anchor.HorizontalFrom), $"unknown origin {(int)anchor.HorizontalFrom}");
            Assert.True(Enum.IsDefined(anchor.VerticalFrom), $"unknown origin {(int)anchor.VerticalFrom}");

            // Only a shape the text ignores can be behind it; anything else would be a
            // contradiction the reader had invented.
            Assert.True(!anchor.BehindText || anchor.Wrapping == TextWrapping.None);
        });
    }

    /// <summary>
    /// The whole corpus, because the store is one structure per file and a reader that walks
    /// it wrongly is far likelier to produce nonsense than nothing.
    /// </summary>
    [Fact]
    public async Task AcrossTheCorpus_EveryFloatingPictureHasReadableBytes()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        List<Picture> pictures = [];

        foreach (string path in ReferenceCorpus.FilesUnder(CorpusRoot, "*.doc"))
        {
            try
            {
                pictures.AddRange(Pictures(await DocReader.LoadAsync(path, cancellationToken)).Where(static p => !p.IsInline));
            }
            catch (Exception error) when (RefusedByDesign.Matches(error))
            {
                // Encrypted and pre-Word 97 files are refused by design.
            }
        }

        Assert.SkipWhen(pictures.Count == 0, ReferenceCorpus.Absent);
        Assert.All(pictures, static picture =>
        {
            Assert.NotEqual(0, picture.Image.Bytes.Length);
            Assert.NotEqual("application/octet-stream", picture.Image.ContentType);
        });
    }

    /// <summary>The text of one part of a saved package, or <see langword="null"/> when it has none.</summary>
    private static string? Package(MemoryStream saved, string part)
    {
        saved.Position = 0;
        using var archive = new System.IO.Compression.ZipArchive(saved, System.IO.Compression.ZipArchiveMode.Read, leaveOpen: true);
        if (archive.GetEntry(part) is not { } entry)
            return null;

        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static async Task<WordDocument> LoadAsync(string relativePath)
    {
        string path = Path.Combine(CorpusRoot, relativePath);
        Assert.SkipUnless(File.Exists(path), ReferenceCorpus.Absent);
        return await DocReader.LoadAsync(path, TestContext.Current.CancellationToken);
    }

    private static List<Picture> Pictures(WordDocument document) =>
    [
        .. document.AllContainers
            .SelectMany(static container => container.Blocks.Paragraphs)
            .SelectMany(static paragraph => paragraph.Objects)
            .Select(static anchored => anchored.Object)
            .OfType<Picture>(),
    ];
}
