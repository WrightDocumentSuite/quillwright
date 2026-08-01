using System.Text;
using Quillwright.Markdown;
using Quillwright.Model;

namespace Quillwright.Html;

/// <summary>Turns a Word document into deterministic, self-contained HTML.</summary>
internal static class HtmlExporter
{
    /// <summary>The small neutral stylesheet a full page carries, so a bare export reads well.</summary>
    private const string BaseStyle =
        "body{font-family:Calibri,'Segoe UI',Arial,sans-serif;line-height:1.4;margin:2rem auto;max-width:52rem;padding:0 1rem;color:#111}\n" +
        "table{border-collapse:collapse;margin:.75rem 0}\n" +
        "td,th{border:1px solid #b0b0b0;padding:.25rem .5rem;vertical-align:top;text-align:left}\n" +
        "pre{background:#f6f6f6;padding:.75rem;overflow-x:auto}\n" +
        "code{font-family:Consolas,'Courier New',monospace}\n" +
        "blockquote{margin:.75rem 2rem;color:#444}\n" +
        "ins{background:#e2f6e2;text-decoration:none}\n" +
        "del{background:#fbe3e4}\n" +
        "img{max-width:100%;height:auto}\n" +
        "hr{border:none;border-top:1px solid #ccc;margin:1.5rem 0}\n" +
        ".footnotes{font-size:.9em;color:#333}\n";

    public static HtmlDocument Export(WordDocument document, HtmlExportOptions options)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(options);
        if (!Enum.IsDefined(options.RevisionMode))
            throw new ArgumentOutOfRangeException(nameof(options), options.RevisionMode, "Unknown revision mode.");
        if (!Enum.IsDefined(options.Images))
            throw new ArgumentOutOfRangeException(nameof(options), options.Images, "Unknown image mode.");

        string mediaDirectoryName = MarkdownPath.NormalizeMediaDirectoryName(options.MediaDirectoryName);
        var diagnostics = new HtmlExportDiagnostics();
        var context = new HtmlContext(document, options, mediaDirectoryName, diagnostics);
        var body = new StringBuilder();

        HtmlWriter.WriteDocument(body, context);

        string text = options.FullDocument ? Page(body.ToString(), document, options) : Finish(body.ToString());
        return new HtmlDocument(text, context.Images, mediaDirectoryName, diagnostics);
    }

    private static string Page(string body, WordDocument document, HtmlExportOptions options)
    {
        string title = options.Title ?? document.Properties.Title ?? "Document";
        string? language = options.Language
            ?? document.Properties.Language
            ?? document.Styles.DefaultRunFormat.Language;

        var page = new StringBuilder();
        page.Append("<!DOCTYPE html>\n<html");
        if (!string.IsNullOrWhiteSpace(language))
            page.Append(" lang=\"").Append(HtmlInlineWriter.Attribute(language)).Append('"');

        page.Append(">\n<head>\n<meta charset=\"utf-8\">\n")
            .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n")
            .Append("<title>").Append(HtmlInlineWriter.Escape(title)).Append("</title>\n")
            .Append("<style>\n").Append(BaseStyle).Append("</style>\n</head>\n<body>\n")
            .Append(body)
            .Append("</body>\n</html>");

        return Finish(page.ToString());
    }

    private static string Finish(string html)
    {
        string trimmed = html.TrimEnd('\n');
        return trimmed + "\n";
    }
}
