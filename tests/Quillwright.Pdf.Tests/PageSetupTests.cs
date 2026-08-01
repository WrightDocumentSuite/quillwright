using Inkwright;
using Quillwright.Model;
using Quillwright.Pdf;
using Quillwright.Primitives;
using Quillwright.Styles;
using Xunit;

namespace Quillwright.Pdf.Tests;

public sealed class PageSetupTests
{
    [Fact]
    public void EmptyDocumentStillProducesOnePage()
    {
        WordDocument document = WordDocument.Create();

        PdfExportResult result = PdfExporter.Render(document);
        using PdfDocument pdf = result.Document;

        Assert.Equal(1, result.PageCount);
        Assert.Single(pdf.Pages);
    }

    [Fact]
    public void PageSizeComesFromTheSection()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].Properties.PageWidth = Length.FromMillimeters(210);
        document.Sections[0].Properties.PageHeight = Length.FromMillimeters(297);

        using PdfDocument pdf = PdfExporter.Render(document).Document;

        Assert.Equal(595.3, pdf.Pages[0].Width, 1);
        Assert.Equal(841.9, pdf.Pages[0].Height, 1);
    }

    [Fact]
    public void LandscapeKeepsTheWidthTheSectionStates()
    {
        WordDocument document = WordDocument.Create();
        SectionProperties properties = document.Sections[0].Properties;
        properties.Orientation = PageOrientation.Landscape;
        properties.PageWidth = Length.FromMillimeters(297);
        properties.PageHeight = Length.FromMillimeters(210);

        using PdfDocument pdf = PdfExporter.Render(document).Document;

        Assert.True(pdf.Pages[0].Width > pdf.Pages[0].Height);
    }

    [Fact]
    public void EachSectionOpensItsOwnPage()
    {
        WordDocument document = WordDocument.Create();
        document.Sections.Add(SectionStart.NextPage);
        document.Sections.Add(SectionStart.NextPage);

        PdfExportResult result = PdfExporter.Render(document);
        using PdfDocument pdf = result.Document;

        Assert.Equal(3, result.PageCount);
    }

    [Fact]
    public void AContinuousSectionCarriesOnTheOpenPage()
    {
        WordDocument document = WordDocument.Create();
        document.Sections.Add(SectionStart.Continuous);

        PdfExportResult result = PdfExporter.Render(document);
        using PdfDocument pdf = result.Document;

        Assert.Equal(1, result.PageCount);
    }

    [Fact]
    public void AnOddPageSectionLeavesABlankPageBehindWhenItHasTo()
    {
        WordDocument document = WordDocument.Create();
        document.Sections.Add(SectionStart.OddPage);

        // Page one is odd, so the second section wants page three and page two is left blank.
        PdfExportResult result = PdfExporter.Render(document);
        using PdfDocument pdf = result.Document;

        Assert.Equal(3, result.PageCount);
    }

    [Fact]
    public void MetadataCrossesOver()
    {
        WordDocument document = WordDocument.Create();
        document.Properties.Title = "Quarterly report";
        document.Properties.Creator = "Ada Lovelace";
        document.Properties.Subject = "Numbers";

        using PdfDocument pdf = PdfExporter.Render(document).Document;

        Assert.Equal("Quarterly report", pdf.Info.Title);
        Assert.Equal("Ada Lovelace", pdf.Info.Author);
        Assert.Equal("Numbers", pdf.Info.Subject);
    }
}
