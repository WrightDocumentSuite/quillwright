using System.IO.Compression;
using System.Text;
using Quillwright.Model;

namespace Quillwright.Tests;

/// <summary>
/// Web extensions — Office add-ins and the state they save with the document ([MS-OWEXML]).
/// No document in the reference corpus carries one, so the parts are written here from the
/// markup Word produces.
/// </summary>
public class WebExtensionTests
{
    private const string TaskPanesPart = "word/webextensions/taskpanes.xml";
    private const string ExtensionPart = "word/webextensions/webextension1.xml";

    [Fact]
    public async Task AWebExtension_SaysWhichAddInItIs()
    {
        WordDocument document = await LoadAsync(await PackageAsync());

        WebExtension extension = Assert.Single(document.WebExtensions);
        Assert.Equal("/" + ExtensionPart, extension.PartPath);
        Assert.Equal("{4BA5E1C0-9C1B-4E2F-8E27-4C61C5F0D111}", extension.Id);
        Assert.Equal("wa104099688", extension.StoreId);
        Assert.Equal("1.0.0.0", extension.Version);
        Assert.Equal("OMEX", extension.StoreType);
    }

    [Fact]
    public async Task AWebExtension_CarriesTheStateItSaved()
    {
        WordDocument document = await LoadAsync(await PackageAsync());

        Assert.Equal(
            new WebExtensionProperty("Office.AutoShowTaskpaneWithDocument", "true"),
            Assert.Single(document.WebExtensions[0].Properties));
    }

    [Fact]
    public async Task AnExtensionShownInATaskPane_KnowsWhereThePaneSits()
    {
        WordDocument document = await LoadAsync(await PackageAsync());

        TaskPaneSettings pane = document.WebExtensions[0].TaskPane!;
        Assert.Equal("right", pane.DockState);
        Assert.Equal(350, pane.Width);
        Assert.Equal(4, pane.Row);
        Assert.False(pane.IsVisible);
    }

    /// <summary>An extension no pane mentions is still in the package and still loads.</summary>
    [Fact]
    public async Task AnExtensionWithNoPane_IsStillRead()
    {
        MemoryStream package = await PackageAsync(withTaskPanes: false);

        WordDocument document = await LoadAsync(package);

        Assert.Null(Assert.Single(document.WebExtensions).TaskPane);
    }

    [Fact]
    public async Task AnOrdinaryDocument_HasNoWebExtensions()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph().AppendText("No add-ins here");

        Assert.Empty((await LoadAsync(await DocumentFixture.SaveAsync(document))).WebExtensions);
    }

    /// <summary>The parts are preserved, so a document keeps its add-in after a round trip.</summary>
    [Fact]
    public async Task ResavingKeepsTheExtension()
    {
        WordDocument document = await LoadAsync(await PackageAsync());

        WordDocument reopened = await LoadAsync(await DocumentFixture.SaveAsync(document));

        Assert.Equal("wa104099688", Assert.Single(reopened.WebExtensions).StoreId);
    }

    private static ValueTask<WordDocument> LoadAsync(MemoryStream package)
    {
        package.Position = 0;
        return WordDocument.LoadAsync(package, cancellationToken: TestContext.Current.CancellationToken);
    }

    private static async Task<MemoryStream> PackageAsync(bool withTaskPanes = true)
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph().AppendText("A document with an add-in");
        using MemoryStream plain = await DocumentFixture.SaveAsync(document);
        return Rewrite(plain, withTaskPanes);
    }

    private static MemoryStream Rewrite(MemoryStream package, bool withTaskPanes)
    {
        package.Position = 0;
        var result = new MemoryStream();
        using (var source = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true))
        using (var target = new ZipArchive(result, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (ZipArchiveEntry entry in source.Entries)
            {
                byte[] content = entry.FullName switch
                {
                    "word/_rels/document.xml.rels" when withTaskPanes => Utf8(WithTaskPanes(Text(entry))),
                    "[Content_Types].xml" => Utf8(WithContentTypes(Text(entry))),
                    _ => Read(entry),
                };

                Write(target, entry.FullName, content);
            }

            if (withTaskPanes)
            {
                Write(target, TaskPanesPart, Utf8(TaskPanes()));
                Write(target, "word/webextensions/_rels/taskpanes.xml.rels", Utf8(TaskPaneRelationships()));
            }

            Write(target, ExtensionPart, Utf8(Extension()));
        }

        result.Position = 0;
        return result;
    }

    private static string WithTaskPanes(string relationships) => relationships.Replace(
        "</Relationships>",
        $"<Relationship Id=\"rIdTaskPanes\" Type=\"{Formats.DocxSchema.RelTaskPanes}\" " +
        "Target=\"webextensions/taskpanes.xml\"/></Relationships>",
        StringComparison.Ordinal);

    private static string WithContentTypes(string contentTypes) => contentTypes.Replace(
        "</Types>",
        $"<Override PartName=\"/{TaskPanesPart}\" ContentType=\"application/vnd.ms-office.webextensiontaskpanes+xml\"/>" +
        $"<Override PartName=\"/{ExtensionPart}\" ContentType=\"application/vnd.ms-office.webextension+xml\"/></Types>",
        StringComparison.Ordinal);

    private static string TaskPanes() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<wetp:taskpanes xmlns:wetp=\"http://schemas.microsoft.com/office/webextensions/taskpanes/2010/11\">" +
        "<wetp:taskpane dockstate=\"right\" visibility=\"0\" width=\"350\" row=\"4\">" +
        "<wetp:webextensionref xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" r:id=\"rId1\"/>" +
        "</wetp:taskpane></wetp:taskpanes>";

    private static string TaskPaneRelationships() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        $"<Relationship Id=\"rId1\" Type=\"{Formats.DocxSchema.RelWebExtension}\" Target=\"webextension1.xml\"/>" +
        "</Relationships>";

    private static string Extension() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<we:webextension xmlns:we=\"http://schemas.microsoft.com/office/webextensions/webextension/2010/11\" " +
        "id=\"{4BA5E1C0-9C1B-4E2F-8E27-4C61C5F0D111}\">" +
        "<we:reference id=\"wa104099688\" version=\"1.0.0.0\" store=\"en-US\" storeType=\"OMEX\"/>" +
        "<we:alternateReferences/><we:properties>" +
        "<we:property name=\"Office.AutoShowTaskpaneWithDocument\" value=\"true\"/>" +
        "</we:properties><we:bindings/><we:snapshot " +
        "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"/></we:webextension>";

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    private static void Write(ZipArchive archive, string name, byte[] content)
    {
        using Stream stream = archive.CreateEntry(name).Open();
        stream.Write(content, 0, content.Length);
    }

    private static byte[] Read(ZipArchiveEntry entry)
    {
        using Stream stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static string Text(ZipArchiveEntry entry) => Encoding.UTF8.GetString(Read(entry));
}
