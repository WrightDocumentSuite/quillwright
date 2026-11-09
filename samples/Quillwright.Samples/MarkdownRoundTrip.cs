using Quillwright.Markdown;
using Quillwright.Model;

namespace Quillwright.Samples;

/// <summary>Converts supported Markdown content to DOCX and back, reporting approximations.</summary>
internal static class MarkdownRoundTrip
{
    public static async Task RunAsync(string directory)
    {
        const string source = "# Release plan\n\nReady for **review**.\n\n" +
            "| Task | Owner |\n| --- | --- |\n| Publish package | Alex |\n";
        MarkdownImportResult imported = MarkdownImporter.Import(source);
        string path = Path.Combine(directory, "06-markdown.docx");
        await imported.Document.SaveAsync(path);

        WordDocument reloaded = await WordDocument.LoadAsync(path);
        MarkdownDocument exported = reloaded.ToMarkdown();
        await exported.SaveAsync(Path.Combine(directory, "markdown-roundtrip"));
        if (!exported.Text.Contains("Release plan", StringComparison.Ordinal))
            throw new InvalidOperationException("The Markdown heading did not round-trip.");

        Console.WriteLine($"  Markdown: {imported.Diagnostics.Count} import and {exported.Diagnostics.Count} export warning(s)");
    }
}
