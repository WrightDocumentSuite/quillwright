using Quillwright.Model;
using Quillwright.Styles;
using Xunit;

namespace Quillwright.Pdf.Tests;

/// <summary>
/// <c>PAGEREF</c> resolved against the pagination the export itself computed — the field a
/// table of contents prints its numbers with, so a TOC in the PDF shows where things actually
/// landed rather than where Word last saw them.
/// </summary>
public sealed class PageRefFieldTests
{
    [Fact]
    public void APageRef_PrintsThePageTheBookmarkLandsOn()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("See page ").AppendField("PAGEREF target \\h", "9");

        Paragraph destination = document.Sections[0].AddParagraph("The destination.");
        destination.Format = destination.Format with { PageBreakBefore = true };
        destination.AddMark(new BookmarkStart { Id = 1, Name = "target" }, 0);
        destination.AddMark(new BookmarkEnd { Id = 1 }, destination.Text.Length);

        using Rendered rendered = Rendered.Of(document);

        Assert.Equal(2, rendered.PageCount);
        Assert.Contains("See page 2", rendered.Text(0), StringComparison.Ordinal);
        Assert.DoesNotContain("9", rendered.Text(0), StringComparison.Ordinal);
    }

    [Fact]
    public void ATocEntry_GetsItsNumberRefreshed()
    {
        WordDocument document = WordDocument.Create();

        // The shape Word gives a TOC entry: the text, a tab, and a PAGEREF at a hidden bookmark.
        document.Sections[0].AddParagraph("Late chapter").AppendText("\t").AppendField("PAGEREF _Toc42 \\h", "99");

        for (int i = 0; i < 2; i++)
        {
            Paragraph filler = document.Sections[0].AddParagraph($"Filler {i}.");
            filler.Format = filler.Format with { PageBreakBefore = true };
        }

        Paragraph heading = document.Sections[0].AddParagraph("Late chapter", "Heading1");
        heading.Format = heading.Format with { PageBreakBefore = true };
        heading.AddMark(new BookmarkStart { Id = 7, Name = "_Toc42" }, 0);
        heading.AddMark(new BookmarkEnd { Id = 7 }, heading.Text.Length);

        using Rendered rendered = Rendered.Of(document);

        Assert.Equal(4, rendered.PageCount);
        Assert.Contains("4", rendered.Text(0), StringComparison.Ordinal);
        Assert.DoesNotContain("99", rendered.Text(0), StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingBookmark_PrintsTheErrorWordPrints()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("See page ").AppendField("PAGEREF nowhere", "3");

        using Rendered rendered = Rendered.Of(document);

        Assert.Contains("Error! Bookmark not defined.", rendered.Text(), StringComparison.Ordinal);
        Assert.Contains(
            rendered.Diagnostics,
            warning => warning.Kind == PdfExportWarningKind.ContentSkipped && warning.Subject == "nowhere");
    }

    /// <summary>
    /// Turning <c>UpdatePageFields</c> off prints every cached result as Word cached it — the
    /// stale <c>PAGEREF</c> included, because a viewer showing an unrepaginated document is
    /// exactly what the option asks for.
    /// </summary>
    [Fact]
    public void UpdatePageFieldsOff_PrintsTheCachedResults()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("Page ").AppendField("PAGE", "7");
        document.Sections[0].AddParagraph("See page ").AppendField("PAGEREF target", "9");

        Paragraph destination = document.Sections[0].AddParagraph("The destination.");
        destination.Format = destination.Format with { PageBreakBefore = true };
        destination.AddMark(new BookmarkStart { Id = 1, Name = "target" }, 0);
        destination.AddMark(new BookmarkEnd { Id = 1 }, destination.Text.Length);

        using Rendered rendered = Rendered.Of(document, new PdfExportOptions { UpdatePageFields = false });

        Assert.Contains("Page 7", rendered.Text(0), StringComparison.Ordinal);
        Assert.Contains("See page 9", rendered.Text(0), StringComparison.Ordinal);
    }
}
