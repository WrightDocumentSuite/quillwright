using Quillwright.Editing;
using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Samples;

/// <summary>
/// Opens a document, changes it and saves it again. Everything the model does not represent
/// — themes, charts, embedded objects — comes through untouched.
/// </summary>
internal static class EditExistingDocument
{
    public static async Task RunAsync(string directory)
    {
        string source = Path.Combine(directory, "01-from-scratch.docx");
        if (!File.Exists(source))
            return;

        WordDocument document = await WordDocument.LoadAsync(source);

        int replaced = document.Replace("Example GmbH", "Beispiel AG");
        document.Highlight("Widget", format => format with { Bold = true, Highlight = HighlightColor.Yellow });

        Paragraph first = document.Paragraphs.First();
        document.AddComment(first, 0, Math.Min(6, first.TextLength), "Check the title wording.", "Reviewer", "R");
        document.AddFootnote(first, "Drafted from the standard template.");

        string path = Path.Combine(directory, "02-edited.docx");
        await document.SaveAsync(path);
        Console.WriteLine($"  edited {Path.GetFileName(path)} ({replaced} replacement(s), {document.LoadDiagnostics.Count} warning(s))");
    }
}
