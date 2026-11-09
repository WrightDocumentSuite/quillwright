using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Quillwright.IO;
using Quillwright.Model;
using Quillwright.Sanitization;
using Xunit;

namespace Quillwright.Tests;

/// <summary>
/// A sanitized copy must agree with itself: what goes from the parts also goes from the
/// relationships, the content types and the references in the XML.
/// </summary>
public sealed class SanitizationTests
{
    private const string Relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PackageRelationships = "http://schemas.openxmlformats.org/package/2006/relationships";

    private static async Task<byte[]> PackageOfAsync(WordDocument document)
    {
        using MemoryStream saved = await DocumentFixture.SaveAsync(document);
        return saved.ToArray();
    }

    private static string Read(ZipArchive archive, string name)
    {
        using var reader = new StreamReader(archive.GetEntry(name)!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private static void Put(ZipArchive archive, string name, string content)
    {
        archive.GetEntry(name)?.Delete();
        using var writer = new StreamWriter(archive.CreateEntry(name).Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static void AddRelationship(ZipArchive archive, string relsPath, string id, string type, string target, bool external = false)
    {
        string relationship = $"<Relationship Id=\"{id}\" Type=\"{type}\" Target=\"{target}\"{(external ? " TargetMode=\"External\"" : string.Empty)}/>";
        string current = archive.GetEntry(relsPath) is null
            ? $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><Relationships xmlns=\"{PackageRelationships}\"></Relationships>"
            : Read(archive, relsPath);
        Put(archive, relsPath, current.Replace("</Relationships>", relationship + "</Relationships>", StringComparison.Ordinal));
    }

    private static void AddContentType(ZipArchive archive, string declaration)
    {
        string types = Read(archive, "[Content_Types].xml");
        Put(archive, "[Content_Types].xml", types.Replace("</Types>", declaration + "</Types>", StringComparison.Ordinal));
    }

    private static void AddBody(ZipArchive archive, string markup)
    {
        string document = Read(archive, "word/document.xml");
        int sectionProperties = document.LastIndexOf("<w:sectPr", StringComparison.Ordinal);
        Put(archive, "word/document.xml", document.Insert(sectionProperties, markup));
    }

    private static byte[] Edit(byte[] package, Action<ZipArchive> edit)
    {
        using var buffer = new MemoryStream();
        buffer.Write(package);
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Update, leaveOpen: true))
            edit(archive);

        return buffer.ToArray();
    }

    private static void DeclareNamespaces(ZipArchive archive)
    {
        string document = Read(archive, "word/document.xml");
        foreach ((string prefix, string uri) in new[] { ("r", Relationships), ("v", "urn:schemas-microsoft-com:vml"), ("o", "urn:schemas-microsoft-com:office:office") })
        {
            string declaration = $"xmlns:{prefix}=\"";
            if (!document.Contains(declaration, StringComparison.Ordinal))
                document = document.Replace("<w:document ", $"<w:document xmlns:{prefix}=\"{uri}\" ", StringComparison.Ordinal);
        }

        Put(archive, "word/document.xml", document);
    }

    /// <summary>A package holding one thing from every category, and ordinary content beside it.</summary>
    private static async Task<WordDocument> EverythingAsync()
    {
        WordDocument source = WordDocument.Create();
        source.Sections[0].AddParagraph("Ordinary text");
        byte[] package = Edit(await PackageOfAsync(source), static archive =>
        {
            DeclareNamespaces(archive);

            // An OLE object and the picture that previews it.
            Put(archive, "word/embeddings/oleObject1.bin", "ole-payload");
            Put(archive, "word/media/preview.png", "preview");
            AddContentType(archive, "<Default Extension=\"bin\" ContentType=\"application/vnd.openxmlformats-officedocument.oleObject\"/>");
            AddContentType(archive, "<Default Extension=\"png\" ContentType=\"image/png\"/>");
            AddRelationship(archive, "word/_rels/document.xml.rels", "rIdOle", Relationships + "/oleObject", "embeddings/oleObject1.bin");
            AddRelationship(archive, "word/_rels/document.xml.rels", "rIdPreview", Relationships + "/image", "media/preview.png");
            AddBody(archive, "<w:p><w:r><w:object w:dxaOrig=\"10\" w:dyaOrig=\"10\"><v:shape id=\"s1\"><v:imagedata r:id=\"rIdPreview\" o:title=\"\"/></v:shape>" +
                "<o:OLEObject Type=\"Embed\" ProgID=\"Package\" ShapeID=\"s1\" DrawAspect=\"Content\" ObjectID=\"_1\" r:id=\"rIdOle\"/></w:object></w:r></w:p>");

            // A link, whose text has to survive it.
            AddRelationship(archive, "word/_rels/document.xml.rels", "rIdLink", Relationships + "/hyperlink", "https://tracker.example/x", external: true);
            AddBody(archive, "<w:p><w:hyperlink r:id=\"rIdLink\"><w:r><w:t>Linked words</w:t></w:r></w:hyperlink></w:p>");

            // A field that reaches outside without a relationship.
            AddBody(archive, "<w:p><w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText> INCLUDEPICTURE \"http://tracker.example/p.png\" </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"end\"/></w:r></w:p>");

            // A custom XML part and a control bound to it.
            Put(archive, "customXml/item1.xml", "<data>private</data>");
            Put(archive, "customXml/itemProps1.xml", "<?xml version=\"1.0\"?><ds:datastoreItem xmlns:ds=\"http://schemas.openxmlformats.org/officeDocument/2006/customXml\" ds:itemID=\"{00000000-0000-0000-0000-000000000001}\"/>");
            AddContentType(archive, "<Override PartName=\"/customXml/itemProps1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.customXmlProperties+xml\"/>");
            AddRelationship(archive, "customXml/_rels/item1.xml.rels", "rId1", Relationships + "/customXmlProps", "itemProps1.xml");
            AddRelationship(archive, "word/_rels/document.xml.rels", "rIdXml", Relationships + "/customXml", "../customXml/item1.xml");
            AddBody(archive, "<w:sdt><w:sdtPr><w:dataBinding w:xpath=\"/data\" w:storeItemID=\"{00000000-0000-0000-0000-000000000001}\"/></w:sdtPr><w:sdtContent><w:p><w:r><w:t>Bound</w:t></w:r></w:p></w:sdtContent></w:sdt>");

            // A signature and the part that lists it.
            Put(archive, "_xmlsignatures/origin.sigs", string.Empty);
            Put(archive, "_xmlsignatures/sig1.xml", "<Signature xmlns=\"http://www.w3.org/2000/09/xmldsig#\"/>");
            AddContentType(archive, "<Default Extension=\"sigs\" ContentType=\"application/vnd.openxmlformats-package.digital-signature-origin\"/>");
            AddContentType(archive, "<Override PartName=\"/_xmlsignatures/sig1.xml\" ContentType=\"application/vnd.openxmlformats-package.digital-signature-xmlsignature+xml\"/>");
            AddRelationship(archive, "_xmlsignatures/_rels/origin.sigs.rels", "rId1", "http://schemas.openxmlformats.org/package/2006/relationships/digital-signature/signature", "sig1.xml");
            AddRelationship(archive, "_rels/.rels", "rIdSig", "http://schemas.openxmlformats.org/package/2006/relationships/digital-signature/origin", "_xmlsignatures/origin.sigs");
        });

        return await WordDocument.LoadAsync(new MemoryStream(package), cancellationToken: TestContext.Current.CancellationToken);
    }

    private static async Task<(byte[] Package, SanitizeReport Report)> SanitizeAsync(WordDocument document, ContentCategories categories)
    {
        using var output = new MemoryStream();
        SanitizeReport report = await document.SaveSanitizedAsync(output, categories, TestContext.Current.CancellationToken);
        return (output.ToArray(), report);
    }

    private static HashSet<string> EntryNames(byte[] package)
    {
        using var archive = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        return new HashSet<string>(archive.Entries.Select(static e => e.FullName), StringComparer.OrdinalIgnoreCase);
    }

    private static string Entry(byte[] package, string name)
    {
        using var archive = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        return Read(archive, name);
    }

    /// <summary>Every internal relationship leads to a part, and every part has a content type.</summary>
    private static void AssertPackageAgrees(byte[] package)
    {
        using var archive = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        HashSet<string> names = new(archive.Entries.Select(static e => OpcPath.ToPartPath(e.FullName)), StringComparer.OrdinalIgnoreCase);

        foreach (ZipArchiveEntry entry in archive.Entries.Where(static e => e.FullName.EndsWith(".rels", StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            string source = OpcPath.GetSourcePart(OpcPath.ToPartPath(entry.FullName));
            foreach (XElement relationship in XDocument.Parse(Read(archive, entry.FullName)).Root!.Elements())
            {
                if ((string?)relationship.Attribute("TargetMode") == "External")
                    continue;

                string target = OpcPath.Resolve(source, (string)relationship.Attribute("Target")!);
                Assert.True(names.Contains(target), $"{entry.FullName} points at {target}, which is not in the package.");
            }
        }

        XElement types = XDocument.Parse(Read(archive, "[Content_Types].xml")).Root!;
        HashSet<string> extensions = new(
            types.Elements().Where(static e => e.Name.LocalName == "Default").Select(static e => (string)e.Attribute("Extension")!),
            StringComparer.OrdinalIgnoreCase);
        HashSet<string> overrides = new(
            types.Elements().Where(static e => e.Name.LocalName == "Override").Select(static e => (string)e.Attribute("PartName")!),
            StringComparer.OrdinalIgnoreCase);

        foreach (string part in overrides)
            Assert.True(names.Contains(part), $"An override names {part}, which is not in the package.");

        foreach (string name in names.Where(static n => !n.Equals("/[Content_Types].xml", StringComparison.OrdinalIgnoreCase)))
            Assert.True(overrides.Contains(name) || extensions.Contains(Path.GetExtension(name).TrimStart('.')), $"{name} has no content type.");
    }

    [Fact]
    public async Task NothingSelectedRemovesNothingAndReportsWhatIsThere()
    {
        WordDocument document = await EverythingAsync();

        (byte[] package, SanitizeReport report) = await SanitizeAsync(document, ContentCategories.None);

        Assert.Empty(report.Removed);
        Assert.Contains("word/embeddings/oleObject1.bin", EntryNames(package));
        Assert.Contains(report.Retained, static item => item.Category == ContentCategories.EmbeddedObjects);
        Assert.Contains(report.Retained, static item => item.Category == ContentCategories.ExternalLinks);
        Assert.Contains(report.Retained, static item => item.Category == ContentCategories.CustomXml);
        Assert.Contains(report.Retained, static item => item.Category == ContentCategories.Signatures);
        AssertPackageAgrees(package);
    }

    [Fact]
    public async Task EmbeddedObjectsGoWithTheirMarkupAndPreview()
    {
        WordDocument document = await EverythingAsync();

        (byte[] package, SanitizeReport report) = await SanitizeAsync(document, ContentCategories.EmbeddedObjects);

        HashSet<string> names = EntryNames(package);
        Assert.DoesNotContain("word/embeddings/oleObject1.bin", names);
        Assert.DoesNotContain("word/media/preview.png", names);

        string body = Entry(package, "word/document.xml");
        Assert.DoesNotContain("<w:object", body, StringComparison.Ordinal);
        Assert.DoesNotContain("rIdOle", Entry(package, "word/_rels/document.xml.rels"), StringComparison.Ordinal);
        Assert.DoesNotContain("rIdPreview", Entry(package, "word/_rels/document.xml.rels"), StringComparison.Ordinal);
        Assert.Contains("Ordinary text", body, StringComparison.Ordinal);

        Assert.Contains(report.Removed, static item => item is { Kind: SanitizedItemKind.Part, Location: "/word/embeddings/oleObject1.bin" });
        Assert.Contains(report.Removed, static item => item is { Kind: SanitizedItemKind.Part, Location: "/word/media/preview.png" });
        Assert.Contains("customXml/item1.xml", names);
        AssertPackageAgrees(package);
    }

    [Fact]
    public async Task APictureUsedElsewhereSurvivesTheObjectThatAlsoUsedIt()
    {
        WordDocument source = WordDocument.Create();
        source.Sections[0].AddParagraph("x");
        byte[] package = Edit(await PackageOfAsync(source), static archive =>
        {
            DeclareNamespaces(archive);
            Put(archive, "word/embeddings/oleObject1.bin", "ole-payload");
            Put(archive, "word/media/shared.png", "shared");
            AddContentType(archive, "<Default Extension=\"bin\" ContentType=\"application/vnd.openxmlformats-officedocument.oleObject\"/>");
            AddContentType(archive, "<Default Extension=\"png\" ContentType=\"image/png\"/>");
            AddRelationship(archive, "word/_rels/document.xml.rels", "rIdOle", Relationships + "/oleObject", "embeddings/oleObject1.bin");
            AddRelationship(archive, "word/_rels/document.xml.rels", "rIdImg", Relationships + "/image", "media/shared.png");
            AddBody(archive, "<w:p><w:r><w:object w:dxaOrig=\"10\" w:dyaOrig=\"10\"><v:shape id=\"s1\"><v:imagedata r:id=\"rIdImg\"/></v:shape><o:OLEObject Type=\"Embed\" ProgID=\"P\" ShapeID=\"s1\" ObjectID=\"_1\" r:id=\"rIdOle\"/></w:object></w:r></w:p>");
            AddBody(archive, "<w:p><w:r><w:pict><v:shape id=\"s2\"><v:imagedata r:id=\"rIdImg\"/></v:shape></w:pict></w:r></w:p>");
        });
        WordDocument document = await WordDocument.LoadAsync(new MemoryStream(package), cancellationToken: TestContext.Current.CancellationToken);

        (byte[] result, _) = await SanitizeAsync(document, ContentCategories.EmbeddedObjects);

        Assert.Contains("word/media/shared.png", EntryNames(result));
        Assert.Contains("rIdImg", Entry(result, "word/_rels/document.xml.rels"), StringComparison.Ordinal);
        AssertPackageAgrees(result);
    }

    [Fact]
    public async Task ExternalLinksGoButTheLinkTextStays()
    {
        WordDocument document = await EverythingAsync();

        (byte[] package, SanitizeReport report) = await SanitizeAsync(document, ContentCategories.ExternalLinks);

        Assert.DoesNotContain("tracker.example/x", Entry(package, "word/_rels/document.xml.rels"), StringComparison.Ordinal);
        string body = Entry(package, "word/document.xml");
        Assert.DoesNotContain("w:hyperlink", body, StringComparison.Ordinal);
        Assert.Contains("Linked words", body, StringComparison.Ordinal);
        Assert.Contains(report.Removed, static item => item is { Kind: SanitizedItemKind.Relationship, Location: "https://tracker.example/x" });
        AssertPackageAgrees(package);
    }

    [Fact]
    public async Task AFieldThatNamesAnOutsideAddressIsReportedAsKept()
    {
        WordDocument document = await EverythingAsync();

        (_, SanitizeReport report) = await SanitizeAsync(document, ContentCategories.ExternalLinks);

        SanitizedItem field = Assert.Single(report.Retained, static item => item.Kind == SanitizedItemKind.FieldInstruction);
        Assert.Contains("tracker.example/p.png", field.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CustomXmlGoesWithItsPropertiesAndBindings()
    {
        WordDocument document = await EverythingAsync();

        (byte[] package, SanitizeReport report) = await SanitizeAsync(document, ContentCategories.CustomXml);

        HashSet<string> names = EntryNames(package);
        Assert.DoesNotContain("customXml/item1.xml", names);
        Assert.DoesNotContain("customXml/itemProps1.xml", names);
        string body = Entry(package, "word/document.xml");
        Assert.DoesNotContain("dataBinding", body, StringComparison.Ordinal);
        Assert.Contains("Bound", body, StringComparison.Ordinal);
        Assert.DoesNotContain("customXmlProperties", Entry(package, "[Content_Types].xml"), StringComparison.Ordinal);
        Assert.Contains(report.Removed, static item => item is { Kind: SanitizedItemKind.Part, Location: "/customXml/itemProps1.xml", Category: ContentCategories.CustomXml });
        AssertPackageAgrees(package);
    }

    [Fact]
    public async Task SignaturesGoWithEverythingTheyList()
    {
        WordDocument document = await EverythingAsync();

        (byte[] package, SanitizeReport report) = await SanitizeAsync(document, ContentCategories.Signatures);

        HashSet<string> names = EntryNames(package);
        Assert.DoesNotContain("_xmlsignatures/origin.sigs", names);
        Assert.DoesNotContain("_xmlsignatures/sig1.xml", names);
        Assert.DoesNotContain("digital-signature", Entry(package, "_rels/.rels"), StringComparison.Ordinal);
        Assert.DoesNotContain("sigs", Entry(package, "[Content_Types].xml"), StringComparison.Ordinal);
        Assert.Contains(report.Removed, static item => item.Category == ContentCategories.Signatures && item.Kind == SanitizedItemKind.Part);
        AssertPackageAgrees(package);
    }

    private static string FixturePath(string name)
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            string candidate = Path.Combine(directory.FullName, "tests", "fixtures", name);
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException(name);
    }

    [Fact]
    public async Task MacrosGoAndTheFileStopsClaimingToHaveThem()
    {
        WordDocument document = await WordDocument.LoadAsync(FixturePath("macros.docm"), cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(document.IsMacroEnabled);

        (byte[] package, SanitizeReport report) = await SanitizeAsync(document, ContentCategories.Macros);

        Assert.DoesNotContain(EntryNames(package), static name => name.Contains("vbaProject", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("macroEnabled", Entry(package, "[Content_Types].xml"), StringComparison.Ordinal);
        Assert.Contains(report.Removed, static item => item is { Kind: SanitizedItemKind.Part, Category: ContentCategories.Macros });
        AssertPackageAgrees(package);

        using var reloadedStream = new MemoryStream(package);
        OpenXmlAssert.Valid(reloadedStream, "macros removed");
        reloadedStream.Position = 0;
        WordDocument reloaded = await WordDocument.LoadAsync(reloadedStream, cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(reloaded.IsMacroEnabled);
        Assert.Equal(document.GetText(), reloaded.GetText());
    }

    [Fact]
    public async Task SanitizingDoesNotChangeTheDocumentObject()
    {
        WordDocument document = await WordDocument.LoadAsync(FixturePath("macros.docm"), cancellationToken: TestContext.Current.CancellationToken);

        await SanitizeAsync(document, ContentCategories.All);
        byte[] again = await PackageOfAsync(document);

        Assert.Contains(EntryNames(again), static name => name.Contains("vbaProject", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AllCategoriesTogetherLeaveAConsistentPackage()
    {
        WordDocument document = await EverythingAsync();

        (byte[] package, SanitizeReport report) = await SanitizeAsync(document, ContentCategories.All);

        Assert.DoesNotContain(EntryNames(package), static name =>
            name.StartsWith("customXml/", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("_xmlsignatures/", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("word/embeddings/", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(report.Retained, static item => item.Kind != SanitizedItemKind.FieldInstruction);
        AssertPackageAgrees(package);

        WordDocument reloaded = await WordDocument.LoadAsync(new MemoryStream(package), cancellationToken: TestContext.Current.CancellationToken);
        Assert.Contains("Ordinary text", reloaded.GetText(), StringComparison.Ordinal);
        Assert.Contains("Linked words", reloaded.GetText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACleanDocumentComesThroughUnharmed()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("Nothing to remove");

        (byte[] package, SanitizeReport report) = await SanitizeAsync(document, ContentCategories.All);

        Assert.Empty(report.Removed);
        Assert.Empty(report.Retained);
        using var stream = new MemoryStream(package);
        OpenXmlAssert.Valid(stream, "clean document");
    }
}
