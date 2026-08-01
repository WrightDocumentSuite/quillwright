using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;
using Xunit;

namespace Quillwright.Pdf.Tests;

public sealed class SectionAndFurnitureTests
{
    private static void Fill(Section section, int paragraphs)
    {
        for (int i = 0; i < paragraphs; i++)
            section.AddParagraph("Body line " + i);
    }

    [Fact]
    public void AHeaderIsDrawnOnEveryPage()
    {
        WordDocument document = WordDocument.Create();
        Section section = document.Sections[0];
        section.Headers.GetOrCreate().AddParagraph("RUNNING HEAD");
        section.Properties.DifferentFirstPage = false;
        Fill(section, 120);

        using Rendered rendered = Rendered.Of(document);

        Assert.True(rendered.PageCount >= 2);
        for (int page = 0; page < rendered.PageCount; page++)
            Assert.Contains("RUNNING HEAD", rendered.Text(page), StringComparison.Ordinal);
    }

    [Fact]
    public void AHeaderSitsAboveTheBody()
    {
        WordDocument document = WordDocument.Create();
        Section section = document.Sections[0];
        section.Headers.GetOrCreate().AddParagraph("HEAD");
        section.Properties.DifferentFirstPage = false;
        section.AddParagraph("Body");

        using Rendered rendered = Rendered.Of(document);

        double head = rendered.Letters().First(letter => letter.Text == "H").Origin.Y;
        double body = rendered.Letters().First(letter => letter.Text == "B").Origin.Y;

        Assert.True(head > body);
    }

    [Fact]
    public void AFooterSitsBelowTheBody()
    {
        WordDocument document = WordDocument.Create();
        Section section = document.Sections[0];
        section.Footers.GetOrCreate().AddParagraph("FOOT");
        section.AddParagraph("Body");

        using Rendered rendered = Rendered.Of(document);

        double foot = rendered.Letters().First(letter => letter.Text == "F").Origin.Y;
        double body = rendered.Letters().First(letter => letter.Text == "B").Origin.Y;

        Assert.True(foot < body);
        Assert.True(foot > 0);
    }

    [Fact]
    public void ATallHeaderPushesTheBodyDown()
    {
        double TopOfBody(int headerLines)
        {
            WordDocument document = WordDocument.Create();
            Section section = document.Sections[0];
            HeaderFooter header = section.Headers.GetOrCreate();
            section.Properties.DifferentFirstPage = false;

            for (int i = 0; i < headerLines; i++)
                header.AddParagraph("Header line " + i);

            section.AddParagraph("Body");

            using Rendered rendered = Rendered.Of(document);
            return rendered.Letters().First(letter => letter.Text == "B").Origin.Y;
        }

        Assert.True(TopOfBody(1) - TopOfBody(12) > 50);
    }

    [Fact]
    public void AFirstPageHeaderReplacesTheUsualOne()
    {
        WordDocument document = WordDocument.Create();
        Section section = document.Sections[0];
        section.Headers.GetOrCreate().AddParagraph("USUAL");
        section.Headers.GetOrCreate(HeaderFooterKind.First).AddParagraph("TITLE PAGE");
        Fill(section, 120);

        using Rendered rendered = Rendered.Of(document);

        Assert.Contains("TITLE PAGE", rendered.Text(0), StringComparison.Ordinal);
        Assert.DoesNotContain("USUAL", rendered.Text(0), StringComparison.Ordinal);
        Assert.Contains("USUAL", rendered.Text(1), StringComparison.Ordinal);
    }

    [Fact]
    public void AnEvenPageHeaderIsUsedOnEvenPages()
    {
        WordDocument document = WordDocument.Create();
        Section section = document.Sections[0];
        section.Properties.DifferentFirstPage = false;
        document.Settings.EvenAndOddHeaders = true;
        section.Headers.GetOrCreate().AddParagraph("ODD");
        section.Headers.GetOrCreate(HeaderFooterKind.Even).AddParagraph("EVEN");
        Fill(section, 120);

        using Rendered rendered = Rendered.Of(document);

        Assert.Contains("ODD", rendered.Text(0), StringComparison.Ordinal);
        Assert.Contains("EVEN", rendered.Text(1), StringComparison.Ordinal);
    }

    [Fact]
    public void ASectionWithNoHeaderInheritsTheOneBefore()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].Headers.GetOrCreate().AddParagraph("SHARED");
        document.Sections[0].Properties.DifferentFirstPage = false;
        document.Sections[0].AddParagraph("First section");

        Section second = document.Sections.Add(SectionStart.NextPage);
        second.AddParagraph("Second section");

        using Rendered rendered = Rendered.Of(document);

        Assert.Equal(2, rendered.PageCount);
        Assert.Contains("SHARED", rendered.Text(1), StringComparison.Ordinal);
    }

    [Fact]
    public void APageFieldPrintsTheNumberOfItsPage()
    {
        WordDocument document = WordDocument.Create();
        Section section = document.Sections[0];
        section.Properties.DifferentFirstPage = false;
        section.Footers.GetOrCreate().AddParagraph().AppendPageNumber();
        Fill(section, 120);

        using Rendered rendered = Rendered.Of(document);

        Assert.True(rendered.PageCount >= 2);
        for (int page = 0; page < rendered.PageCount; page++)
        {
            string expected = (page + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            Assert.Contains(expected, rendered.Text(page), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ANumPagesFieldPrintsTheTotal()
    {
        WordDocument document = WordDocument.Create();
        Section section = document.Sections[0];
        section.Properties.DifferentFirstPage = false;

        Paragraph footer = section.Footers.GetOrCreate().AddParagraph();
        footer.AppendText("Page ");
        footer.AppendPageNumber();
        footer.AppendText(" of ");
        footer.AppendPageCount();

        Fill(section, 120);

        using Rendered rendered = Rendered.Of(document);
        string total = rendered.PageCount.ToString(System.Globalization.CultureInfo.InvariantCulture);

        Assert.Contains("Page 1 of " + total, rendered.Text(0), StringComparison.Ordinal);
    }

    [Fact]
    public void TheSectionDecidesHowPageNumbersAreWritten()
    {
        WordDocument document = WordDocument.Create();
        Section section = document.Sections[0];
        section.Properties.DifferentFirstPage = false;
        section.Properties.PageNumbering.Format = ListNumberFormat.LowerRoman;
        section.Footers.GetOrCreate().AddParagraph().AppendPageNumber();
        Fill(section, 120);

        using Rendered rendered = Rendered.Of(document);

        Assert.Contains("ii", rendered.Text(1), StringComparison.Ordinal);
    }

    [Fact]
    public void ASectionCanRestartTheNumbering()
    {
        WordDocument document = WordDocument.Create();
        Section first = document.Sections[0];
        first.Properties.DifferentFirstPage = false;
        first.Footers.GetOrCreate().AddParagraph().AppendPageNumber();
        first.AddParagraph("One");

        Section second = document.Sections.Add(SectionStart.NextPage);
        second.Properties.PageNumbering.Start = 1;
        second.AddParagraph("Two");

        using Rendered rendered = Rendered.Of(document);

        Assert.Equal(2, rendered.PageCount);
        Assert.Contains("1", rendered.Text(1), StringComparison.Ordinal);
    }

    [Fact]
    public void EachSectionKeepsItsOwnPageSize()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("Portrait");

        Section landscape = document.Sections.Add(SectionStart.NextPage);
        landscape.Properties.Orientation = PageOrientation.Landscape;
        landscape.Properties.PageWidth = Length.FromMillimeters(297);
        landscape.Properties.PageHeight = Length.FromMillimeters(210);
        landscape.AddParagraph("Landscape");

        using Rendered rendered = Rendered.Of(document);

        Assert.True(rendered.Document.Pages[0].Height > rendered.Document.Pages[0].Width);
        Assert.True(rendered.Document.Pages[1].Width > rendered.Document.Pages[1].Height);
    }

    [Fact]
    public void AnInlinePictureIsDrawnInTheLine()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph();
        paragraph.AppendText("Before ");
        paragraph.AppendPicture(
            ImageData.FromBytes(Pixels.Png(40, 20)),
            Length.FromPoints(40),
            Length.FromPoints(20));
        paragraph.AppendText(" after");

        using Rendered rendered = Rendered.Of(document);

        Assert.Single(rendered.Document.Pages[0].Images);
        Assert.Contains("Before", rendered.Text(), StringComparison.Ordinal);
        Assert.Contains("after", rendered.Text(), StringComparison.Ordinal);
    }

    [Fact]
    public void AnInlinePictureTakesRoomOnTheLine()
    {
        double AfterX(bool withPicture)
        {
            WordDocument document = WordDocument.Create();
            Paragraph paragraph = document.Sections[0].AddParagraph();
            paragraph.AppendText("A");

            if (withPicture)
            {
                paragraph.AppendPicture(
                    ImageData.FromBytes(Pixels.Png(40, 20)),
                    Length.FromPoints(40),
                    Length.FromPoints(20));
            }

            paragraph.AppendText("Z");

            using Rendered rendered = Rendered.Of(document);
            return rendered.Letters().First(letter => letter.Text == "Z").Origin.X;
        }

        Assert.Equal(40, AfterX(withPicture: true) - AfterX(withPicture: false), 0.5);
    }

    [Fact]
    public void AFloatingPictureIsPlacedAgainstThePage()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph("Text");
        Picture picture = paragraph.AppendPicture(
            ImageData.FromBytes(Pixels.Png(30, 30)),
            Length.FromPoints(30),
            Length.FromPoints(30));

        picture.IsInline = false;
        picture.Anchor = new PictureAnchor
        {
            HorizontalFrom = AnchorOrigin.Page,
            VerticalFrom = AnchorOrigin.Page,
            OffsetX = Length.FromPoints(100),
            OffsetY = Length.FromPoints(200),
            Wrapping = TextWrapping.None,
        };

        using Rendered rendered = Rendered.Of(document);

        Assert.Single(rendered.Document.Pages[0].Images);

        // A floating picture takes no room on the line, so the text still starts at the margin.
        Assert.Equal(72, rendered.LeftEdge(), 1);
    }

    [Fact]
    public void AnUnsupportedImageIsReportedRatherThanDrawn()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph();
        paragraph.AppendPicture(
            ImageData.FromBytes(new byte[] { 0x42, 0x4D, 0x00, 0x01 }, "image/bmp"),
            Length.FromPoints(20),
            Length.FromPoints(20));

        using Rendered rendered = Rendered.Of(document);

        Assert.Empty(rendered.Document.Pages[0].Images);
        Assert.Contains(rendered.Diagnostics, warning => warning.Kind == PdfExportWarningKind.ImageSkipped);
    }
}
