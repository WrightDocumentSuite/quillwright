using Quillwright.Html;
using Quillwright.Model;

namespace Quillwright.Samples;

/// <summary>Imports an editor fragment and writes a DOCX plus a self-contained HTML preview.</summary>
internal static class HtmlPreview
{
    public static async Task RunAsync(string directory)
    {
        const string source = "<h1>Project brief</h1><p>Prepared for <strong>Northwind Ltd</strong>.</p>" +
            "<ul><li>Confirm scope</li><li>Approve delivery</li></ul>";
        HtmlImportResult imported = HtmlImporter.Import(source);
        await imported.Document.SaveAsync(Path.Combine(directory, "07-html-import.docx"));

        WordDocument reloaded = await WordDocument.LoadAsync(Path.Combine(directory, "07-html-import.docx"));
        var preview = reloaded.ToHtml();
        string previewDirectory = Path.Combine(directory, "html-preview");
        await preview.SaveAsync(previewDirectory);
        string html = await File.ReadAllTextAsync(Path.Combine(previewDirectory, "document.html"));
        if (!html.Contains("Project brief", StringComparison.Ordinal))
            throw new InvalidOperationException("The HTML preview is missing its heading.");

        Console.WriteLine($"  HTML preview: {imported.Diagnostics.Count} import warning(s); pagination remains in DOCX");
    }
}
