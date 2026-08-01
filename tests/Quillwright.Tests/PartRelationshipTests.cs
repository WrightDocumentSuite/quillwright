using System.IO.Compression;
using System.Xml.Linq;
using Quillwright.Formats;
using Quillwright.Model;

namespace Quillwright.Tests;

/// <summary>
/// A relationship id belongs to the part whose markup carries it. The same <c>rId1</c> may
/// legitimately mean styles in the document, an image in a header and a hyperlink in a footer.
/// </summary>
public class PartRelationshipTests
{
    private static readonly XNamespace PackageRelationships = DocxSchema.NsPackageRelationships;
    private static readonly XNamespace Relationships = DocxSchema.NsRelationships;

    [Fact]
    public async Task AHeaderPicture_UsesTheHeaderRelationshipAndLoadsIntoTheModel()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("body");
        HeaderFooter header = document.Sections[0].Headers.GetOrCreate();
        header.AddParagraph().AppendPicture(ImageData.FromBytes(TestImages.Png));

        using MemoryStream saved = await DocumentFixture.SaveAsync(document);

        AssertPartRelationship(saved, "word/header1.xml", DocxSchema.RelImage, "media/image1.png", "embed");
        AssertNoRelationship(saved, "word/document.xml", DocxSchema.RelImage);

        saved.Position = 0;
        WordDocument reopened = await WordDocument.LoadAsync(saved, cancellationToken: TestContext.Current.CancellationToken);
        Picture picture = Assert.Single(reopened.Sections[0].Headers.Default!.Blocks.Paragraphs
            .SelectMany(static paragraph => paragraph.Objects)
            .Select(static entry => entry.Object)
            .OfType<Picture>());

        Assert.Single(reopened.Media);
        Assert.Equal(TestImages.Png, picture.Image.Bytes);
        Assert.DoesNotContain(reopened.LoadDiagnostics, static warning => warning.Code == Diagnostics.WarningCode.UnresolvedMedia);
    }

    [Fact]
    public async Task AFooterHyperlink_UsesTheFooterRelationshipAndKeepsItsTarget()
    {
        const string target = "https://example.com/from-footer";
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("body");
        Paragraph paragraph = document.Sections[0].Footers.GetOrCreate().AddParagraph("the site");
        paragraph.AddRange(new Hyperlink { Url = target }, 0, paragraph.TextLength);

        using MemoryStream saved = await DocumentFixture.SaveAsync(document);

        AssertPartRelationship(saved, "word/footer1.xml", DocxSchema.RelHyperlink, target, "id");
        AssertNoRelationship(saved, "word/document.xml", DocxSchema.RelHyperlink);

        saved.Position = 0;
        WordDocument reopened = await WordDocument.LoadAsync(saved, cancellationToken: TestContext.Current.CancellationToken);
        Hyperlink link = Assert.Single(reopened.Sections[0].Footers.Default!.Blocks.Paragraphs
            .SelectMany(static p => p.Ranges)
            .Select(static entry => entry.Range)
            .OfType<Hyperlink>());
        Assert.Equal(target, link.Url);
    }

    [Fact]
    public async Task AFootnoteHyperlink_UsesTheFootnotesRelationship()
    {
        const string target = "https://example.com/from-footnote";
        WordDocument document = WordDocument.Create();
        Paragraph body = document.Sections[0].AddParagraph("body");
        Note footnote = document.AddFootnote(body, "footnote link");
        AddLink(footnote.Blocks.Paragraphs.Single(), target);

        using MemoryStream saved = await DocumentFixture.SaveAsync(document);

        AssertPartRelationship(saved, "word/footnotes.xml", DocxSchema.RelHyperlink, target, "id");
        AssertNoRelationship(saved, "word/document.xml", DocxSchema.RelHyperlink);

        saved.Position = 0;
        WordDocument reopened = await WordDocument.LoadAsync(saved, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(target, LinkIn(reopened.Footnotes.Single(static note => note.Kind == NoteKind.Normal)).Url);
    }

    [Fact]
    public async Task ACommentHyperlink_UsesTheCommentsRelationship()
    {
        const string target = "https://example.com/from-comment";
        WordDocument document = WordDocument.Create();
        Paragraph body = document.Sections[0].AddParagraph("body text");
        Comment comment = document.AddComment(body, 0, 4, "comment link", "Reviewer", "R");
        AddLink(comment.Blocks.Paragraphs.Single(), target);

        using MemoryStream saved = await DocumentFixture.SaveAsync(document);

        AssertPartRelationship(saved, "word/comments.xml", DocxSchema.RelHyperlink, target, "id");
        AssertNoRelationship(saved, "word/document.xml", DocxSchema.RelHyperlink);

        saved.Position = 0;
        WordDocument reopened = await WordDocument.LoadAsync(saved, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(target, LinkIn(reopened.Comments.Single()).Url);
    }

    [Fact]
    public async Task TheCorpusFooterHyperlink_SurvivesAgainstTheOriginalDocument()
    {
        string fixture = ReferenceCorpus.RequireTelerikPath(
            "Flow/Tests/Flow/TestDocuments/TextSearch/SampleDocument.docx");
        WordDocument original = await WordDocument.LoadAsync(
            fixture, cancellationToken: TestContext.Current.CancellationToken);
        string[] before = FooterLinks(original);
        Assert.NotEmpty(before);

        using MemoryStream saved = await DocumentFixture.SaveAsync(original);
        saved.Position = 0;
        WordDocument reopened = await WordDocument.LoadAsync(
            saved, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(before, FooterLinks(reopened));
    }

    [Fact]
    public async Task TheCorpusHeaderWatermarkImage_ResolvesAgainstTheHeaderPart()
    {
        string fixture = ReferenceCorpus.RequireTelerikPath(
            "Flow/Tests/Flow/TestDocuments/Watermarks/WatermarkWithImageInsideHeader.docx");
        WordDocument original = await WordDocument.LoadAsync(
            fixture, cancellationToken: TestContext.Current.CancellationToken);
        byte[][] before = [.. original.Media.Select(static image => image.Bytes.ToArray())];

        Assert.NotEmpty(before);
        Assert.DoesNotContain(original.LoadDiagnostics,
            static warning => warning.Code == Diagnostics.WarningCode.UnresolvedMedia);

        using MemoryStream saved = await DocumentFixture.SaveAsync(original);
        saved.Position = 0;
        WordDocument reopened = await WordDocument.LoadAsync(
            saved, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(before.Length, reopened.Media.Count);
        Assert.All(before, bytes => Assert.Contains(
            reopened.Media, image => image.Bytes.Span.SequenceEqual(bytes)));
        Assert.DoesNotContain(reopened.LoadDiagnostics,
            static warning => warning.Code == Diagnostics.WarningCode.UnresolvedMedia);
    }

    private static void AddLink(Paragraph paragraph, string target) =>
        paragraph.AddRange(new Hyperlink { Url = target }, 0, paragraph.TextLength);

    private static Hyperlink LinkIn(BlockContainer container) => Assert.Single(container.Blocks.Paragraphs
        .SelectMany(static paragraph => paragraph.Ranges)
        .Select(static entry => entry.Range)
        .OfType<Hyperlink>());

    private static string[] FooterLinks(WordDocument document) =>
    [
        .. document.AllContainers
            .SelectMany(static container => container.Blocks.Paragraphs)
            .SelectMany(static paragraph => paragraph.Ranges)
            .Select(static entry => entry.Range)
            .OfType<Hyperlink>()
            .Select(static link => link.Url)
            .Where(static url => url is not null)
            .Select(static url => url!)
            .Order(StringComparer.Ordinal),
    ];

    private static void AssertPartRelationship(
        MemoryStream package, string sourcePart, string relationshipType, string target, string referenceAttribute)
    {
        XDocument relationships = ReadXml(package, RelationshipsEntry(sourcePart));
        XElement relationship = Assert.Single(
            relationships.Root!.Elements(PackageRelationships + "Relationship"),
            element => element.Attribute("Type")?.Value == relationshipType);

        Assert.Equal(target, relationship.Attribute("Target")?.Value);
        string id = Assert.IsType<XAttribute>(relationship.Attribute("Id")).Value;

        XDocument source = ReadXml(package, sourcePart);
        XAttribute reference = Assert.Single(source.Descendants()
            .Attributes(Relationships + referenceAttribute));
        Assert.Equal(id, reference.Value);
    }

    private static void AssertNoRelationship(MemoryStream package, string sourcePart, string relationshipType)
    {
        XDocument relationships = ReadXml(package, RelationshipsEntry(sourcePart));
        Assert.DoesNotContain(relationships.Root!.Elements(PackageRelationships + "Relationship"),
            element => element.Attribute("Type")?.Value == relationshipType);
    }

    private static string RelationshipsEntry(string sourcePart)
    {
        int slash = sourcePart.LastIndexOf('/');
        return string.Concat(sourcePart.AsSpan(0, slash + 1), "_rels/", sourcePart.AsSpan(slash + 1), ".rels");
    }

    private static XDocument ReadXml(MemoryStream package, string entryName)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        ZipArchiveEntry entry = Assert.IsType<ZipArchiveEntry>(archive.GetEntry(entryName));
        using Stream stream = entry.Open();
        return XDocument.Load(stream, LoadOptions.PreserveWhitespace);
    }
}
