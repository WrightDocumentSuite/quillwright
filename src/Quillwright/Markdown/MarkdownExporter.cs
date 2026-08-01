using System.Text;
using Quillwright.Model;

namespace Quillwright.Markdown;

/// <summary>Turns a Word document into deterministic Markdown plus its sidecar images.</summary>
internal static class MarkdownExporter
{
    public static MarkdownDocument Export(WordDocument document, MarkdownExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);
        Validate(options);

        string mediaDirectoryName = MarkdownPath.NormalizeMediaDirectoryName(options.MediaDirectoryName);
        var diagnostics = new MarkdownExportDiagnostics();
        var context = new MarkdownContext(document, options, mediaDirectoryName, diagnostics);
        var builder = new StringBuilder();

        MarkdownBlockWriter.WriteDocument(builder, context);
        MarkdownNoteWriter.WriteDefinitions(builder, context);

        return new MarkdownDocument(Finish(builder), context.Media.Images, mediaDirectoryName, diagnostics);
    }

    private static void Validate(MarkdownExportOptions options)
    {
        if (!Enum.IsDefined(options.Flavor))
            throw new ArgumentOutOfRangeException(nameof(options), options.Flavor, "Unknown Markdown flavor.");
        if (!Enum.IsDefined(options.RevisionMode))
            throw new ArgumentOutOfRangeException(nameof(options), options.RevisionMode, "Unknown revision mode.");
    }

    /// <summary>Closes the document with exactly one LF.</summary>
    private static string Finish(StringBuilder builder)
    {
        while (builder.Length > 0 && builder[^1] == '\n')
            builder.Length--;

        builder.Append('\n');
        return builder.ToString();
    }
}
