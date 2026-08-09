using Inkwright;
using Inkwright.Fonts;
using Quillwright.Model;
using Quillwright.Pdf.Fonts;
using Quillwright.Styles;
using Xunit;

namespace Quillwright.Pdf.Tests;

public sealed class FontMapTests
{
    private static (FontMap Map, PdfExportDiagnostics Diagnostics, PdfDocument Pdf) Create(PdfExportOptions? options = null)
    {
        var pdf = PdfDocument.Create();
        var diagnostics = new PdfExportDiagnostics();
        return (new FontMap(pdf, options ?? new PdfExportOptions(), diagnostics), diagnostics, pdf);
    }

    [Fact]
    public void AFamilyNobodyHasFallsBackToABuiltInFont()
    {
        (FontMap map, PdfExportDiagnostics diagnostics, PdfDocument pdf) = Create(new PdfExportOptions
        {
            FallbackFontFamily = "No Such Family Either",
        });

        using (pdf)
        {
            PdfFont font = map.Resolve("Absolutely No Such Family", bold: false, italic: false);

            Assert.IsType<Standard14PdfFont>(font);
            Assert.Contains("Absolutely No Such Family", diagnostics.SubstitutedFonts);
        }
    }

    [Fact]
    public void ASerifNameFallsBackToASerifBuiltIn()
    {
        (FontMap map, _, PdfDocument pdf) = Create(new PdfExportOptions { FallbackFontFamily = "Nothing At All" });

        using (pdf)
        {
            var font = (Standard14PdfFont)map.Resolve("Imaginary Old Style Roman", bold: true, italic: false);
            Assert.Equal("Times-Bold", font.Metrics.Name);
        }
    }

    [Fact]
    public void AMonospacedNameFallsBackToCourier()
    {
        (FontMap map, _, PdfDocument pdf) = Create(new PdfExportOptions { FallbackFontFamily = "Nothing At All" });

        using (pdf)
        {
            var font = (Standard14PdfFont)map.Resolve("Imaginary Mono", bold: false, italic: true);
            Assert.Equal("Courier-Oblique", font.Metrics.Name);
        }
    }

    [Fact]
    public void TheSameFamilyResolvesToTheSameFontObject()
    {
        (FontMap map, _, PdfDocument pdf) = Create();

        using (pdf)
        {
            PdfFont first = map.Resolve("Arial", bold: false, italic: false);
            PdfFont second = map.Resolve("Arial", bold: false, italic: false);
            Assert.Same(first, second);
        }
    }

    [Fact]
    public void BoldAndRegularAreDifferentFonts()
    {
        (FontMap map, _, PdfDocument pdf) = Create();

        using (pdf)
        {
            PdfFont regular = map.Resolve("Arial", bold: false, italic: false);
            PdfFont bold = map.Resolve("Arial", bold: true, italic: false);
            Assert.NotSame(regular, bold);
        }
    }

    [Fact]
    public void AThemeSlotResolvesToTheThemeDefault()
    {
        (FontMap map, _, PdfDocument pdf) = Create();

        using (pdf)
        {
            PdfFont themed = map.Resolve(RunFormat.Default with { FontAsciiTheme = "minorHAnsi" });
            PdfFont named = map.Resolve("Calibri", bold: false, italic: false);
            Assert.Same(named, themed);
        }
    }

    [Fact]
    public void AnExplicitFileWinsOverTheMachine()
    {
        string? arial = SystemFonts.Find("Arial");
        Assert.SkipWhen(arial is null, "This machine has no Arial to point the option at.");

        var options = new PdfExportOptions();
        options.FontFiles["Totally Made Up"] = arial!;
        (FontMap map, PdfExportDiagnostics diagnostics, PdfDocument pdf) = Create(options);

        using (pdf)
        {
            PdfFont font = map.Resolve("Totally Made Up", bold: false, italic: false);

            Assert.IsType<EmbeddedTrueTypeFont>(font);
            Assert.Empty(diagnostics.SubstitutedFonts);
        }
    }

    [Fact]
    public void WindowsOfficeAndPdfAliasesResolveToTimesNewRomanWithoutSubstitution()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "This regression covers Windows font aliases.");
        string regular = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", "times.ttf");
        Assert.SkipWhen(!File.Exists(regular), "Times New Roman is not installed.");

        (FontMap map, PdfExportDiagnostics diagnostics, PdfDocument pdf) = Create();
        using (pdf)
        {
            var cyrillicAlias = Assert.IsType<EmbeddedTrueTypeFont>(
                map.Resolve("Times New Roman CYR", bold: false, italic: false));
            var pdfAlias = Assert.IsType<EmbeddedTrueTypeFont>(
                map.Resolve("Times-Roman", bold: true, italic: false));

            Assert.Equal("Times New Roman", cyrillicAlias.Program.FamilyName);
            Assert.Equal("Times New Roman", pdfAlias.Program.FamilyName);
            Assert.Empty(diagnostics.SubstitutedFonts);
        }
    }
}
