using System.Text;

namespace Quillwright.Markdown;

internal static class MarkdownNoteWriter
{
    public static void WriteDefinitions(StringBuilder builder, MarkdownContext context)
    {
        if (context.Notes.Entries.Count == 0)
            return;

        if (context.Options.Flavor == MarkdownFlavor.GitHub)
            WriteGitHub(builder, context);
        else
            WriteCommonMarkHtml(builder, context);
    }

    private static void WriteGitHub(StringBuilder builder, MarkdownContext context)
    {
        // Rendering one note may encounter a reference to another, so use a growing indexed loop.
        for (int index = 0; index < context.Notes.Entries.Count; index++)
        {
            MarkdownNoteEntry note = context.Notes.Entries[index];
            string body = MarkdownBlockWriter.RenderBlocks(note.Body.Blocks, context);
            string[] lines = (body.Length == 0 ? " " : body).Split('\n');
            var definition = new StringBuilder();
            definition.Append("[^").Append(note.Label).Append("]: ").Append(lines[0]);
            for (int line = 1; line < lines.Length; line++)
                definition.Append('\n').Append("    ").Append(lines[line]);
            MarkdownBlockWriter.AppendBlock(builder, definition.ToString());
        }
    }

    private static void WriteCommonMarkHtml(StringBuilder builder, MarkdownContext context)
    {
        var html = new StringBuilder();
        html.Append("<section class=\"footnotes\">\n<ol>\n");
        for (int index = 0; index < context.Notes.Entries.Count; index++)
        {
            MarkdownNoteEntry note = context.Notes.Entries[index];
            html.Append("<li id=\"").Append(MarkdownText.HtmlAttribute(note.Label)).Append("\">")
                .Append(MarkdownHtmlWriter.RenderBlocks(note.Body.Blocks, context)).Append("</li>\n");
        }

        html.Append("</ol>\n</section>");
        MarkdownBlockWriter.AppendBlock(builder, html.ToString());
        context.Diagnostics.Add(
            MarkdownExportWarningKind.HtmlFallbackUsed,
            "CommonMark has no footnote syntax, so notes use generated HTML.",
            "commonmark-notes");
    }
}
