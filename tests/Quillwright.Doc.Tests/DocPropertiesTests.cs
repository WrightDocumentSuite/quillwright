using Quillwright.Model;
using Quillwright.Primitives;

namespace Quillwright.Doc.Tests;

/// <summary>
/// The block of document-wide settings is not optional and is easy to get wrong in a way
/// nothing complains about: its flags are single bits with names that do not match the ones
/// the newer format uses for the same thing.
/// </summary>
public class DocPropertiesTests
{
    [Fact]
    public void ADocumentWithOneHeader_DoesNotAskForFacingPages()
    {
        // The lowest bit of the properties block means "even and odd pages differ". Setting
        // it on a document with a single header leaves the even pages with none at all.
        WordDocument document = WordDocument.Create();
        document.Sections[0].Blocks.Add(new Paragraph("body"));
        document.Sections[0].Headers.GetOrCreate().Blocks.Add(new Paragraph("the only header"));

        Assert.False(RoundTrip(document).Settings.EvenAndOddHeaders);
    }

    [Fact]
    public void ADocumentWithAnEvenPageHeader_AsksForFacingPages()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].Blocks.Add(new Paragraph("body"));
        document.Sections[0].Headers.GetOrCreate().Blocks.Add(new Paragraph("odd pages"));
        document.Sections[0].Headers.GetOrCreate(Styles.HeaderFooterKind.Even).Blocks.Add(new Paragraph("even pages"));

        Assert.True(RoundTrip(document).Settings.EvenAndOddHeaders);
    }

    [Fact]
    public void TheSettingSurvivesEvenWithNoEvenPageHeader()
    {
        WordDocument document = WordDocument.Create();
        document.Settings.EvenAndOddHeaders = true;
        document.Sections[0].Blocks.Add(new Paragraph("body"));

        Assert.True(RoundTrip(document).Settings.EvenAndOddHeaders);
    }

    [Fact]
    public void TheDefaultTabStop_SurvivesTheRoundTrip()
    {
        WordDocument document = WordDocument.Create();
        document.Settings.DefaultTabStop = Length.FromCentimeters(1.5);
        document.Sections[0].Blocks.Add(new Paragraph("body"));

        Assert.Equal(Length.FromCentimeters(1.5).Twips, RoundTrip(document).Settings.DefaultTabStop.Twips);
    }

    [Fact]
    public void TheDocumentsOwnProperties_SurviveTheRoundTrip()
    {
        var created = new DateTimeOffset(2019, 3, 4, 9, 30, 0, TimeSpan.Zero);
        WordDocument document = WordDocument.Create();
        document.Sections[0].Blocks.Add(new Paragraph("body"));
        document.Properties.Title = "Quarterly report";
        document.Properties.Creator = "Ada Lovelace";
        document.Properties.Subject = "Numbers";
        document.Properties.Keywords = "report, numbers";
        document.Properties.LastModifiedBy = "Grace Hopper";
        document.Properties.Created = created;

        DocumentProperties reopened = RoundTrip(document).Properties;

        Assert.Equal("Quarterly report", reopened.Title);
        Assert.Equal("Ada Lovelace", reopened.Creator);
        Assert.Equal("Numbers", reopened.Subject);
        Assert.Equal("report, numbers", reopened.Keywords);
        Assert.Equal("Grace Hopper", reopened.LastModifiedBy);
        Assert.Equal(created, reopened.Created!.Value.ToUniversalTime());
    }

    [Fact]
    public void NonLatinProperties_SurviveTheRoundTrip()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].Blocks.Add(new Paragraph("body"));
        document.Properties.Title = "Квартальный отчёт";
        document.Properties.Creator = "Ада Лавлейс";

        DocumentProperties reopened = RoundTrip(document).Properties;

        Assert.Equal("Квартальный отчёт", reopened.Title);
        Assert.Equal("Ада Лавлейс", reopened.Creator);
    }

    [Fact]
    public void ADocumentWithNoProperties_GetsNoSummaryStream()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].Blocks.Add(new Paragraph("body"));

        CompoundFile container = CompoundFile.Open(DocWriter.Save(document));

        Assert.DoesNotContain(container.StreamNames, static name => name.EndsWith("SummaryInformation", StringComparison.Ordinal));
    }

    private static WordDocument RoundTrip(WordDocument document) => DocReader.Load(DocWriter.Save(document));
}
