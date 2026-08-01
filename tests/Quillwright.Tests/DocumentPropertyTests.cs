using System.IO.Compression;
using Quillwright.Model;

namespace Quillwright.Tests;

/// <summary>
/// The two property parts beyond the core one: the statistics the writing program records
/// (<c>docProps/app.xml</c>) and the free-form fields a document management system keeps
/// (<c>docProps/custom.xml</c>).
/// </summary>
public class DocumentPropertyTests
{
    [Fact]
    public async Task CustomProperties_SurviveTheRoundTrip()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("body");
        document.CustomProperties.Set("Reviewer", "A. Editor");
        document.CustomProperties.Set("Revision", 4L);
        document.CustomProperties.Set("Approved", true);
        document.CustomProperties.Set("Weight", 1.5);
        document.CustomProperties.Set("Signed", new DateTimeOffset(2026, 7, 1, 9, 30, 0, TimeSpan.Zero));

        WordDocument reloaded = await DocumentFixture.RoundTripAsync(document, "custom properties");

        Assert.Equal("A. Editor", reloaded.CustomProperties["Reviewer"]?.Value.AsText());
        Assert.Equal(4L, reloaded.CustomProperties["Revision"]?.Value.AsInteger());
        Assert.True(reloaded.CustomProperties["Approved"]?.Value.AsBoolean());
        Assert.Equal(1.5, reloaded.CustomProperties["Weight"]?.Value.AsReal());
        Assert.Equal(
            new DateTimeOffset(2026, 7, 1, 9, 30, 0, TimeSpan.Zero),
            reloaded.CustomProperties["Signed"]?.Value.AsDateTime());
    }

    /// <summary>
    /// Identifiers start at two: zero and one are reserved for the dictionary and the code
    /// page of the OLE property set the part corresponds to.
    /// </summary>
    [Fact]
    public async Task CustomProperties_AreNumberedFromTwo()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("body");
        document.CustomProperties.Set("First", "one");
        document.CustomProperties.Set("Second", "two");

        using MemoryStream saved = await DocumentFixture.SaveAsync(document);
        string part = ReadPart(saved, "docProps/custom.xml");

        Assert.Contains("pid=\"2\" name=\"First\"", part, StringComparison.Ordinal);
        Assert.Contains("pid=\"3\" name=\"Second\"", part, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SettingAPropertyTwice_ReplacesItRatherThanRepeatingIt()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("body");
        document.CustomProperties.Set("Status", "draft");
        document.CustomProperties.Set("Status", "final");

        WordDocument reloaded = await DocumentFixture.RoundTripAsync(document, "a replaced property");

        Assert.Single(reloaded.CustomProperties);
        Assert.Equal("final", reloaded.CustomProperties["Status"]?.Value.AsText());
    }

    [Fact]
    public async Task ApplicationProperties_SurviveTheRoundTrip()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("body");
        document.ApplicationProperties.Application = "Quillwright";
        document.ApplicationProperties.Company = "Acme";
        document.ApplicationProperties.Words = 1;
        document.ApplicationProperties.LinksUpToDate = false;

        WordDocument reloaded = await DocumentFixture.RoundTripAsync(document, "application properties");

        Assert.Equal("Quillwright", reloaded.ApplicationProperties.Application);
        Assert.Equal("Acme", reloaded.ApplicationProperties.Company);
        Assert.Equal(1, reloaded.ApplicationProperties.Words);
        Assert.False(reloaded.ApplicationProperties.LinksUpToDate);
    }

    /// <summary>
    /// <c>CT_Properties</c> declares its children in a fixed order, so an element written
    /// after the fact has to land at its schema position rather than at the end.
    /// </summary>
    [Fact]
    public async Task ApplicationProperties_AreWrittenInSchemaOrder()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("body");
        document.ApplicationProperties.Application = "Quillwright";
        document.ApplicationProperties.Company = "Acme";
        document.ApplicationProperties.Pages = 2;

        using MemoryStream saved = await DocumentFixture.SaveAsync(document);
        string part = ReadPart(saved, "docProps/app.xml");

        Assert.InRange(
            part.IndexOf("<Company>", StringComparison.Ordinal),
            0,
            part.IndexOf("<Pages>", StringComparison.Ordinal));
        Assert.InRange(
            part.IndexOf("<Pages>", StringComparison.Ordinal),
            0,
            part.IndexOf("<Application>", StringComparison.Ordinal));
        OpenXmlAssert.Valid(saved, "application properties in schema order");
    }

    /// <summary>
    /// The vectors the model does not interpret sit between the properties it does, so
    /// rewriting the part has to leave them exactly where they were.
    /// </summary>
    [Fact]
    public async Task TheVectorsOfTheApplicationPart_SurviveAnEditAroundThem()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("body");
        document.ApplicationProperties.SetRaw(
            "TitlesOfParts",
            "<TitlesOfParts><vt:vector size=\"1\" baseType=\"lpstr\"><vt:lpstr>Chapter one</vt:lpstr></vt:vector></TitlesOfParts>");

        document.ApplicationProperties.Company = "Acme";
        WordDocument reloaded = await DocumentFixture.RoundTripAsync(document, "an untouched vector");

        Assert.Contains("Chapter one", reloaded.ApplicationProperties.GetRaw("TitlesOfParts"), StringComparison.Ordinal);
        Assert.Equal("Acme", reloaded.ApplicationProperties.Company);
    }

    [Fact]
    public async Task ADocumentWithNoSuchProperties_GrowsNeitherPart()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("body");

        using MemoryStream saved = await DocumentFixture.SaveAsync(document);
        saved.Position = 0;
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);

        Assert.Null(archive.GetEntry("docProps/app.xml"));
        Assert.Null(archive.GetEntry("docProps/custom.xml"));
    }

    private static string ReadPart(MemoryStream package, string name)
    {
        package.Position = 0;
        using var archive = new ZipArchive(package, ZipArchiveMode.Read, leaveOpen: true);
        using var reader = new StreamReader(archive.GetEntry(name)!.Open());
        return reader.ReadToEnd();
    }
}
