using Quillwright.Doc;
using Quillwright.Model;
using Quillwright.Pdf;
using Quillwright.Rtf;
using Quillwright.Templates;

var document = WordDocument.Create();
document.Sections[0].AddParagraph("{{Value}}");

TemplateResult templateResult = WordTemplateEngine.Render(document, new SmokeTemplate("package smoke"));
if (templateResult.ValuesFilled != 1 || document.GetText() != "package smoke")
    throw new InvalidOperationException("The packaged template generator or runtime did not fill the model.");

WordDocument docRoundTrip = DocReader.Load(DocWriter.Save(document));
if (!docRoundTrip.GetText().Contains("package smoke", StringComparison.Ordinal))
    throw new InvalidOperationException("The packaged DOC reader/writer did not round-trip the document.");

RtfExportResult rtf = RtfWriter.Save(document);
RtfImportResult rtfRoundTrip = RtfReader.Load(rtf.Content.Span);
if (!rtfRoundTrip.Document.GetText().Contains("package smoke", StringComparison.Ordinal))
    throw new InvalidOperationException("The packaged RTF reader/writer did not round-trip the document.");

var pdfOptions = new PdfExportOptions { Tagged = true };
PdfExportResult pdfResult = PdfExporter.Render(document, pdfOptions);
byte[] pdf;
using (pdfResult.Document)
    pdf = pdfResult.Document.ToArray();

if (pdf.Length < 5 || !pdf.AsSpan(0, 5).SequenceEqual("%PDF-"u8))
    throw new InvalidOperationException("The packaged PDF exporter did not render a PDF document.");

if (OperatingSystem.IsLinux() && !pdfResult.Diagnostics.Any(static warning =>
        warning.Kind == PdfExportWarningKind.FontSubstituted &&
        warning.Subject == "Calibri" &&
        warning.Message.Contains("'Carlito'", StringComparison.Ordinal)))
{
    throw new InvalidOperationException(
        "The Linux package smoke did not use the expected Carlito substitute for Calibri.");
}

Console.WriteLine(
    $"Package smoke passed: {typeof(WordDocument).Assembly.GetName().Version}; " +
    $"PDF bytes={pdf.Length}; template={SmokeTemplate.TemplateBinder.GetType().Name}.");

[WordTemplate]
public partial record SmokeTemplate(string Value);
