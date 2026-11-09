using Quillwright.Model;
using Xunit;

namespace Quillwright.Pdf.Tests;

/// <summary>
/// By default a render carries on past what it cannot draw and says so afterwards. A caller can
/// instead name the compromises that must stop it, and then gets no document at all.
/// </summary>
public sealed class StrictExportTests
{
    private const string MissingFamily = "No Such Family Anywhere";

    private static WordDocument WithUnrenderableBlock()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("Terms");
        document.Sections[0].Blocks.Add(new RawBlock("<w:customXml xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"/>"));
        return document;
    }

    private static WordDocument WithMissingFont()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph().AppendText("Terms", new Styles.RunFormat { FontAscii = MissingFamily, FontHighAnsi = MissingFamily });
        return document;
    }

    [Fact]
    public void ByDefaultARenderCarriesOnPastWhatItCannotDraw()
    {
        using Rendered rendered = Rendered.Of(WithUnrenderableBlock());

        Assert.Contains(rendered.Diagnostics, static warning => warning.Kind == PdfExportWarningKind.ContentSkipped);
        Assert.Contains("Terms", rendered.Text(), StringComparison.Ordinal);
    }

    [Fact]
    public void ARefusedOmissionStopsTheRenderAndNamesWhatWasRefused()
    {
        var options = new PdfExportOptions { RefuseWhen = PdfExportRefusal.Omissions };

        PdfExportRefusedException exception = Assert.Throws<PdfExportRefusedException>(
            () => PdfExporter.Render(WithUnrenderableBlock(), options));

        PdfExportWarning refused = Assert.Single(exception.Refused);
        Assert.Equal(PdfExportWarningKind.ContentSkipped, refused.Kind);
        Assert.Contains(refused, exception.Diagnostics);
        Assert.Contains(refused.Message, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARefusedRenderCreatesNoFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf");
        var options = new PdfExportOptions { RefuseWhen = PdfExportRefusal.Omissions };

        Assert.Throws<PdfExportRefusedException>(() => WithUnrenderableBlock().SaveAsPdf(path, options));

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void ARefusedRenderLeavesAnExistingFileAlone()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf");
        byte[] earlier = [1, 2, 3];
        File.WriteAllBytes(path, earlier);

        try
        {
            var options = new PdfExportOptions { RefuseWhen = PdfExportRefusal.Omissions };

            Assert.Throws<PdfExportRefusedException>(() => WithUnrenderableBlock().SaveAsPdf(path, options));

            Assert.Equal(earlier, File.ReadAllBytes(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ADocumentWithNoRefusedCompromiseIsProduced()
    {
        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph("Terms");
        var options = new PdfExportOptions { RefuseWhen = PdfExportRefusal.Omissions };

        using Rendered rendered = Rendered.Of(document, options);

        Assert.Contains("Terms", rendered.Text(), StringComparison.Ordinal);
    }

    [Fact]
    public void AFontSubstitutionIsOnlyRefusedWhenThePolicySaysSo()
    {
        var lenient = new PdfExportOptions { RefuseWhen = PdfExportRefusal.Omissions };
        using (Rendered.Of(WithMissingFont(), lenient))
        {
        }

        var strict = new PdfExportOptions { RefuseWhen = PdfExportRefusal.OmissionsAndFontSubstitutions };
        PdfExportRefusedException exception = Assert.Throws<PdfExportRefusedException>(
            () => PdfExporter.Render(WithMissingFont(), strict));

        Assert.Contains(exception.Refused, static warning => warning.Subject == MissingFamily);
    }

    [Fact]
    public void APolicyCanSingleOutOneFamily()
    {
        var allowsIt = new PdfExportOptions
        {
            RefuseWhen = static warning => warning.Kind == PdfExportWarningKind.FontSubstituted && warning.Subject == "Some Other Family",
        };
        using (Rendered.Of(WithMissingFont(), allowsIt))
        {
        }

        var refusesIt = new PdfExportOptions
        {
            RefuseWhen = static warning => warning.Kind == PdfExportWarningKind.FontSubstituted && warning.Subject == MissingFamily,
        };
        Assert.Throws<PdfExportRefusedException>(() => PdfExporter.Render(WithMissingFont(), refusesIt));
    }

    [Fact]
    public void EveryRefusedCompromiseIsReportedAtOnce()
    {
        WordDocument document = WithUnrenderableBlock();
        document.Sections[0].AddParagraph().AppendText("More", new Styles.RunFormat { FontAscii = MissingFamily, FontHighAnsi = MissingFamily });
        var options = new PdfExportOptions { RefuseWhen = PdfExportRefusal.AnyCompromise };

        PdfExportRefusedException exception = Assert.Throws<PdfExportRefusedException>(() => PdfExporter.Render(document, options));

        Assert.Contains(exception.Refused, static warning => warning.Kind == PdfExportWarningKind.ContentSkipped);
        Assert.Contains(exception.Refused, static warning => warning.Kind == PdfExportWarningKind.FontSubstituted);
    }
}
