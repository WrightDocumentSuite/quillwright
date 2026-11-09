using System.IO.Compression;
using System.Security.Cryptography;
using Quillwright.IO;
using Quillwright.Model;
using Quillwright.Primitives;
using Xunit;

namespace Quillwright.Tests;

/// <summary>
/// Saving is not the end of a document's life: it can be edited and saved again, and every part it
/// gains has to find a name that nothing else in the package already holds.
/// </summary>
public sealed class RepeatedSaveTests
{
    private static readonly byte[] FirstImage = TestImages.Png;
    private static readonly byte[] SecondImage = [.. TestImages.Png, 0x42];
    private static readonly byte[] ThirdImage = [.. TestImages.Png, 0x43];

    private static readonly Length Size = Length.FromCentimeters(1);

    private static string Hash(ReadOnlyMemory<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes.Span));

    private static async Task<byte[]> PackageAsync(WordDocument document)
    {
        using MemoryStream buffer = await DocumentFixture.SaveAsync(document);
        return buffer.ToArray();
    }

    private static async Task<WordDocument> LoadAsync(byte[] package) =>
        await WordDocument.LoadAsync(new MemoryStream(package), cancellationToken: TestContext.Current.CancellationToken);

    private static string[] EntryNames(byte[] package)
    {
        using var archive = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        return [.. archive.Entries.Select(static entry => entry.FullName)];
    }

    private static string[] MediaHashes(byte[] package)
    {
        using var archive = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        return
        [
            .. archive.Entries
                .Where(static entry => entry.FullName.StartsWith("word/media/", StringComparison.Ordinal))
                .Select(static entry =>
                {
                    using Stream stream = entry.Open();
                    return Convert.ToHexString(SHA256.HashData(stream));
                })
                .Order(StringComparer.Ordinal),
        ];
    }

    private static string[] Hashes(params byte[][] images) =>
        [.. images.Select(static image => Hash(image)).Order(StringComparer.Ordinal)];

    private static void AssertEveryEntryOnce(byte[] package)
    {
        string[] names = EntryNames(package);
        Assert.Equal(names.Length, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    private static Picture[] PicturesIn(BlockContainer container) =>
        [.. container.Blocks.OfType<Paragraph>().SelectMany(static p => p.Objects).Select(static o => o.Object).OfType<Picture>()];

    [Fact]
    public async Task ImageAddedAfterSaveDoesNotReplaceTheFirst()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("a").AppendPicture(ImageData.FromBytes(FirstImage), Size, Size);
        await PackageAsync(document);

        document.Sections[0].AddParagraph("b").AppendPicture(ImageData.FromBytes(SecondImage), Size, Size);
        byte[] package = await PackageAsync(document);

        AssertEveryEntryOnce(package);
        Assert.Equal(Hashes(FirstImage, SecondImage), MediaHashes(package));

        WordDocument reloaded = await LoadAsync(package);
        Picture[] pictures = PicturesIn(reloaded.Sections[0]);
        Assert.Equal(Hash(FirstImage), Hash(pictures[0].Image.Bytes));
        Assert.Equal(Hash(SecondImage), Hash(pictures[1].Image.Bytes));
    }

    [Fact]
    public async Task HeaderAddedAfterSaveDoesNotReplaceTheFirst()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("first");
        document.Sections[0].Headers.GetOrCreate().AddParagraph("FIRST HEADER");
        await PackageAsync(document);

        Section second = document.Sections.Add();
        second.AddParagraph("second");
        second.Headers.GetOrCreate().AddParagraph("SECOND HEADER");
        byte[] package = await PackageAsync(document);

        AssertEveryEntryOnce(package);
        WordDocument reloaded = await LoadAsync(package);
        Assert.Equal("FIRST HEADER", reloaded.Sections[0].Headers.Default?.GetText());
        Assert.Equal("SECOND HEADER", reloaded.Sections[1].Headers.Default?.GetText());
    }

    [Fact]
    public async Task FooterAddedAfterSaveDoesNotReplaceTheFirst()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("first");
        document.Sections[0].Footers.GetOrCreate().AddParagraph("FIRST FOOTER");
        await PackageAsync(document);

        Section second = document.Sections.Add();
        second.AddParagraph("second");
        second.Footers.GetOrCreate().AddParagraph("SECOND FOOTER");
        byte[] package = await PackageAsync(document);

        AssertEveryEntryOnce(package);
        WordDocument reloaded = await LoadAsync(package);
        Assert.Equal("FIRST FOOTER", reloaded.Sections[0].Footers.Default?.GetText());
        Assert.Equal("SECOND FOOTER", reloaded.Sections[1].Footers.Default?.GetText());
    }

    [Fact]
    public async Task LoadedDocumentKeepsItsPartsWhenGivenNewOnes()
    {
        WordDocument original = WordDocument.Create();
        original.Sections[0].AddParagraph("a").AppendPicture(ImageData.FromBytes(FirstImage), Size, Size);
        original.Sections[0].Headers.GetOrCreate().AddParagraph("FIRST HEADER");
        original.Sections[0].Footers.GetOrCreate().AddParagraph("FIRST FOOTER");
        byte[] saved = await PackageAsync(original);

        WordDocument document = await LoadAsync(saved);
        Section second = document.Sections.Add();
        second.AddParagraph("b").AppendPicture(ImageData.FromBytes(SecondImage), Size, Size);
        second.Headers.GetOrCreate().AddParagraph("SECOND HEADER");
        second.Footers.GetOrCreate().AddParagraph("SECOND FOOTER");
        byte[] package = await PackageAsync(document);

        AssertEveryEntryOnce(package);
        Assert.Equal(Hashes(FirstImage, SecondImage), MediaHashes(package));

        WordDocument reloaded = await LoadAsync(package);
        Assert.Equal("FIRST HEADER", reloaded.Sections[0].Headers.Default?.GetText());
        Assert.Equal("SECOND HEADER", reloaded.Sections[1].Headers.Default?.GetText());
        Assert.Equal("FIRST FOOTER", reloaded.Sections[0].Footers.Default?.GetText());
        Assert.Equal("SECOND FOOTER", reloaded.Sections[1].Footers.Default?.GetText());
    }

    [Fact]
    public async Task NewImageAvoidsMediaOnlyAHeaderRefersTo()
    {
        WordDocument original = WordDocument.Create();
        original.Sections[0].AddParagraph("body");
        original.Sections[0].Headers.GetOrCreate().AddParagraph("h")
            .AppendPicture(ImageData.FromBytes(FirstImage), Size, Size);
        byte[] saved = await PackageAsync(original);

        WordDocument document = await LoadAsync(saved);
        document.Sections[0].AddParagraph("b").AppendPicture(ImageData.FromBytes(SecondImage), Size, Size);
        byte[] package = await PackageAsync(document);

        AssertEveryEntryOnce(package);
        Assert.Equal(Hashes(FirstImage, SecondImage), MediaHashes(package));

        WordDocument reloaded = await LoadAsync(package);
        Assert.Equal(Hash(FirstImage), Hash(PicturesIn(reloaded.Sections[0].Headers.Default!).Single().Image.Bytes));
        Assert.Equal(Hash(SecondImage), Hash(PicturesIn(reloaded.Sections[0]).Single().Image.Bytes));
    }

    [Fact]
    public async Task PictureAddedToAnotherStoryBetweenSavesKeepsEveryImage()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("a").AppendPicture(ImageData.FromBytes(FirstImage), Size, Size);
        await PackageAsync(document);

        document.Sections[0].Headers.GetOrCreate().AddParagraph("h")
            .AppendPicture(ImageData.FromBytes(SecondImage), Size, Size);
        document.Sections[0].Footers.GetOrCreate().AddParagraph("f")
            .AppendPicture(ImageData.FromBytes(ThirdImage), Size, Size);
        byte[] package = await PackageAsync(document);

        AssertEveryEntryOnce(package);
        Assert.Equal(Hashes(FirstImage, SecondImage, ThirdImage), MediaHashes(package));

        WordDocument reloaded = await LoadAsync(package);
        Assert.Equal(Hash(SecondImage), Hash(PicturesIn(reloaded.Sections[0].Headers.Default!).Single().Image.Bytes));
        Assert.Equal(Hash(ThirdImage), Hash(PicturesIn(reloaded.Sections[0].Footers.Default!).Single().Image.Bytes));
    }

    [Fact]
    public async Task ImageFromAnotherDocumentDoesNotTakeOverAnExistingPart()
    {
        WordDocument donor = WordDocument.Create();
        ImageData foreign = ImageData.FromBytes(SecondImage);
        donor.Sections[0].AddParagraph("x").AppendPicture(foreign, Size, Size);
        await PackageAsync(donor);

        WordDocument original = WordDocument.Create();
        original.Sections[0].AddParagraph("a").AppendPicture(ImageData.FromBytes(FirstImage), Size, Size);
        byte[] saved = await PackageAsync(original);

        WordDocument document = await LoadAsync(saved);
        document.Sections[0].AddParagraph("b").AppendPicture(foreign, Size, Size);
        byte[] package = await PackageAsync(document);

        AssertEveryEntryOnce(package);
        Assert.Equal(Hashes(FirstImage, SecondImage), MediaHashes(package));

        WordDocument reloaded = await LoadAsync(package);
        Picture[] pictures = PicturesIn(reloaded.Sections[0]);
        Assert.Equal(Hash(FirstImage), Hash(pictures[0].Image.Bytes));
        Assert.Equal(Hash(SecondImage), Hash(pictures[1].Image.Bytes));
    }

    [Fact]
    public async Task SavingUnchangedDocumentAgainKeepsTheSameParts()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("a").AppendPicture(ImageData.FromBytes(FirstImage), Size, Size);
        document.Sections[0].Headers.GetOrCreate().AddParagraph("HEADER");

        string[] first = EntryNames(await PackageAsync(document));
        string[] second = EntryNames(await PackageAsync(document));

        Assert.Equal(first.Order(StringComparer.Ordinal), second.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task WritingAPartTwiceIsRefused()
    {
        using var buffer = new MemoryStream();
        OpcPackage package = await OpcPackage.CreateAsync(buffer, leaveOpen: true, CancellationToken.None);
        await using (package)
        {
            await package.WriteRawPartAsync("/word/media/image1.png", [1], CancellationToken.None);
            await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await package.WriteRawPartAsync("/WORD/media/image1.png", [2], CancellationToken.None));
        }
    }
}
