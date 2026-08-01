using System.Globalization;
using System.Text;
using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Markdown;

/// <summary>Turns semantic inline tokens into Markdown and generated inline HTML.</summary>
internal static class MarkdownInlineWriter
{
    public static string Render(Paragraph paragraph, MarkdownContext context, bool tableCell = false)
    {
        List<MarkdownInlineToken> tokens = Coalesce(MarkdownInlineWalker.Walk(paragraph, context));
        var builder = new StringBuilder(paragraph.TextLength + 16);
        bool lineStart = true;

        for (int i = 0; i < tokens.Count;)
        {
            Hyperlink? link = tokens[i].Link;
            if (link is null)
            {
                WriteToken(builder, tokens[i++], context, tableCell, ref lineStart);
                continue;
            }

            int end = i + 1;
            while (end < tokens.Count && ReferenceEquals(tokens[end].Link, link))
                end++;

            WriteLinkedGroup(builder, tokens, i, end, link, context, tableCell, ref lineStart);
            i = end;
        }

        return builder.ToString();
    }

    /// <summary>
    /// Joins adjacent text tokens whose distilled style agrees. The walker keeps runs apart
    /// when their resolved formatting differs — a format-rich exporter needs that — but two
    /// runs Markdown renders identically must come out as one stretch, not two markers.
    /// </summary>
    private static List<MarkdownInlineToken> Coalesce(List<MarkdownInlineToken> tokens)
    {
        var joined = new List<MarkdownInlineToken>(tokens.Count);
        foreach (MarkdownInlineToken token in tokens)
        {
            if (token.Kind == MarkdownInlineKind.Text &&
                joined.Count > 0 &&
                joined[^1] is { Kind: MarkdownInlineKind.Text } previous &&
                previous.Style == token.Style &&
                ReferenceEquals(previous.Link, token.Link))
            {
                previous.Text += token.Text;
                continue;
            }

            joined.Add(token);
        }

        return joined;
    }

    public static string RenderPlain(Paragraph paragraph, MarkdownContext context)
    {
        var builder = new StringBuilder(paragraph.TextLength);
        foreach (MarkdownInlineToken token in MarkdownInlineWalker.Walk(paragraph, context))
        {
            switch (token.Kind)
            {
                case MarkdownInlineKind.Text:
                    builder.Append(token.Text);
                    break;
                case MarkdownInlineKind.LineBreak or MarkdownInlineKind.BlockBreak:
                    builder.Append('\n');
                    break;
                case MarkdownInlineKind.Tab:
                    builder.Append('\t');
                    break;
                case MarkdownInlineKind.NoteReference:
                    builder.Append("[note]");
                    break;
                case MarkdownInlineKind.Picture:
                    context.Diagnostics.Add(
                        MarkdownExportWarningKind.ContentSkipped,
                        "A picture inside a code paragraph is not embedded in the code block.",
                        "picture-in-code");
                    break;
            }
        }

        return builder.ToString();
    }

    private static void WriteLinkedGroup(
        StringBuilder builder,
        List<MarkdownInlineToken> tokens,
        int start,
        int end,
        Hyperlink link,
        MarkdownContext context,
        bool tableCell,
        ref bool lineStart)
    {
        if (tokens.GetRange(start, end - start).Any(static token =>
                token.Kind is MarkdownInlineKind.NoteReference or MarkdownInlineKind.Anchor or MarkdownInlineKind.BlockBreak))
        {
            context.Diagnostics.Add(
                MarkdownExportWarningKind.StructureApproximated,
                "A hyperlink around a note, bookmark, or block break was flattened to plain content.",
                "complex-hyperlink-content");
            for (int i = start; i < end; i++)
                WriteToken(builder, tokens[i], context, tableCell, ref lineStart);
            return;
        }

        string? destination = Destination(link, context);
        var label = new StringBuilder();
        bool labelLineStart = lineStart;
        for (int i = start; i < end; i++)
            WriteToken(label, tokens[i], context, tableCell, ref labelLineStart);

        if (destination is null || label.Length == 0)
        {
            builder.Append(label);
            lineStart = labelLineStart;
            return;
        }

        try
        {
            builder.Append('[').Append(label).Append("](")
                .Append(MarkdownText.LinkDestination(destination)).Append(')');
            lineStart = false;
        }
        catch (ArgumentException)
        {
            context.Diagnostics.Add(
                MarkdownExportWarningKind.UnsafeLinkSkipped,
                "A hyperlink target containing control characters was not emitted.",
                "link-control-character");
            builder.Append(label);
            lineStart = labelLineStart;
        }

        if (!string.IsNullOrEmpty(link.Tooltip) || !string.IsNullOrEmpty(link.TargetFrame))
        {
            context.Diagnostics.Add(
                MarkdownExportWarningKind.FormattingDropped,
                "Hyperlink tooltip and target-frame metadata are not represented in Markdown.",
                "hyperlink-metadata");
        }
    }

    private static void WriteToken(
        StringBuilder builder,
        MarkdownInlineToken token,
        MarkdownContext context,
        bool tableCell,
        ref bool lineStart)
    {
        switch (token.Kind)
        {
            case MarkdownInlineKind.Text:
                WriteText(builder, token.Text ?? string.Empty, token.Style, context, tableCell, ref lineStart);
                break;
            case MarkdownInlineKind.LineBreak:
                builder.Append(tableCell ? "<br>" : "  \n");
                lineStart = true;
                break;
            case MarkdownInlineKind.BlockBreak:
                builder.Append(tableCell ? "<br>" : "\n\n");
                lineStart = true;
                break;
            case MarkdownInlineKind.Tab:
                builder.Append(' ');
                context.Diagnostics.Add(
                    MarkdownExportWarningKind.StructureApproximated,
                    "A tab outside a code block is represented by one space.",
                    "tab");
                break;
            case MarkdownInlineKind.Anchor:
                builder.Append("<a id=\"").Append(MarkdownText.HtmlAttribute(token.Text)).Append("\"></a>");
                break;
            case MarkdownInlineKind.Picture when token.Picture is { } picture:
                WritePicture(builder, picture, context);
                lineStart = false;
                break;
            case MarkdownInlineKind.NoteReference when token.NoteReference is { } reference:
                WriteNoteReference(builder, reference, context);
                lineStart = false;
                break;
        }
    }

    private static void WriteText(
        StringBuilder builder,
        string text,
        MarkdownInlineStyle style,
        MarkdownContext context,
        bool tableCell,
        ref bool lineStart)
    {
        if (text.Length == 0)
            return;

        if (style.Code)
        {
            if (style.Bold || style.Italic || style.Strike || style.Underline ||
                style.VerticalAlignment != VerticalTextAlignment.Baseline)
            {
                context.Diagnostics.Add(
                    MarkdownExportWarningKind.FormattingDropped,
                    "Formatting nested inside inline code is represented only by the code span.",
                    "inline-code-formatting");
            }

            builder.Append(MarkdownText.CodeSpan(text));
            lineStart = false;
            return;
        }

        int leading = 0;
        while (leading < text.Length && char.IsWhiteSpace(text[leading]))
            leading++;
        int trailing = text.Length;
        while (trailing > leading && char.IsWhiteSpace(text[trailing - 1]))
            trailing--;

        MarkdownText.Append(builder, text.AsSpan(0, leading), tableCell, ref lineStart);
        if (trailing > leading)
        {
            var core = new StringBuilder(trailing - leading + 8);
            MarkdownText.Append(core, text.AsSpan(leading, trailing - leading), tableCell, ref lineStart);
            string formatted = ApplyStyle(core.ToString(), style, context.Options.Flavor);
            builder.Append(formatted);
        }

        MarkdownText.Append(builder, text.AsSpan(trailing), tableCell, ref lineStart);
    }

    private static string ApplyStyle(string text, MarkdownInlineStyle style, MarkdownFlavor flavor)
    {
        string result = text;
        if (style.Bold && style.Italic)
            result = $"***{result}***";
        else if (style.Bold)
            result = $"**{result}**";
        else if (style.Italic)
            result = $"*{result}*";

        if (style.Strike)
            result = flavor == MarkdownFlavor.GitHub ? $"~~{result}~~" : $"<del>{result}</del>";
        if (style.Underline)
            result = $"<ins>{result}</ins>";
        result = style.VerticalAlignment switch
        {
            VerticalTextAlignment.Superscript => $"<sup>{result}</sup>",
            VerticalTextAlignment.Subscript => $"<sub>{result}</sub>",
            _ => result,
        };
        return result;
    }

    private static void WritePicture(StringBuilder builder, Picture picture, MarkdownContext context)
    {
        string fileName = context.Media.Add(picture.Image);
        string reference = context.Media.Reference(fileName);
        string alt = !string.IsNullOrWhiteSpace(picture.Description)
            ? picture.Description
            : !string.IsNullOrWhiteSpace(picture.Name) ? picture.Name : fileName;

        bool hasSize = picture.Width.Twips > 0 && picture.Height.Twips > 0;
        bool naturalKnown = picture.Image.NaturalWidth.Twips > 0 && picture.Image.NaturalHeight.Twips > 0;
        bool resized = !naturalKnown ||
                       Math.Abs(picture.Width.Twips - picture.Image.NaturalWidth.Twips) > 20 ||
                       Math.Abs(picture.Height.Twips - picture.Image.NaturalHeight.Twips) > 20;

        if (context.Options.PreserveImageDimensions && hasSize && resized)
        {
            int width = Math.Max(1, (int)Math.Round(picture.Width.ToPixels(), MidpointRounding.AwayFromZero));
            int height = Math.Max(1, (int)Math.Round(picture.Height.ToPixels(), MidpointRounding.AwayFromZero));
            builder.Append("<img src=\"").Append(MarkdownText.HtmlAttribute(reference))
                .Append("\" alt=\"").Append(MarkdownText.HtmlAttribute(alt))
                .Append("\" width=\"").Append(width.ToString(CultureInfo.InvariantCulture))
                .Append("\" height=\"").Append(height.ToString(CultureInfo.InvariantCulture)).Append("\">");
            context.Diagnostics.Add(
                MarkdownExportWarningKind.HtmlFallbackUsed,
                "A resized image uses generated HTML to preserve its displayed dimensions.",
                "image-dimensions");
            return;
        }

        builder.Append("![").Append(MarkdownText.Escape(alt)).Append("](")
            .Append(MarkdownText.LinkDestination(reference)).Append(')');
    }

    private static void WriteNoteReference(
        StringBuilder builder,
        NoteReference reference,
        MarkdownContext context)
    {
        if (context.Notes.Add(reference) is not { } note)
        {
            builder.Append("<sup>?</sup>");
            return;
        }

        if (context.Options.Flavor == MarkdownFlavor.GitHub)
        {
            builder.Append("[^").Append(note.Label).Append(']');
            return;
        }

        builder.Append("<sup><a href=\"#").Append(MarkdownText.HtmlAttribute(note.Label)).Append("\">")
            .Append(note.Number.ToString(CultureInfo.InvariantCulture)).Append("</a></sup>");
    }

    internal static string? Destination(Hyperlink link, MarkdownContext context)
    {
        if (!string.IsNullOrWhiteSpace(link.Url))
        {
            string destination = link.Url;
            if (destination.Any(c => c is '\r' or '\n' or '\0' || char.IsControl(c)))
            {
                context.Diagnostics.Add(
                    MarkdownExportWarningKind.UnsafeLinkSkipped,
                    "A hyperlink target containing control characters was not emitted.",
                    "link-control-character");
                return null;
            }

            if (Uri.TryCreate(destination, UriKind.Absolute, out Uri? uri) &&
                uri.Scheme is "javascript" or "vbscript" or "data")
            {
                context.Diagnostics.Add(
                    MarkdownExportWarningKind.UnsafeLinkSkipped,
                    "A potentially executable hyperlink target was not emitted.",
                    uri.Scheme.ToLowerInvariant());
                return null;
            }

            return destination;
        }

        if (!string.IsNullOrWhiteSpace(link.Anchor))
        {
            if (context.Anchors.Resolve(link.Anchor) is { } id)
                return "#" + id;

            context.Diagnostics.Add(
                MarkdownExportWarningKind.StructureApproximated,
                "An internal hyperlink points to a bookmark that is not exported.",
                "missing-bookmark");
            return null;
        }

        context.Diagnostics.Add(
            MarkdownExportWarningKind.StructureApproximated,
            "A hyperlink without a target was flattened to its label.",
            "empty-hyperlink");
        return null;
    }
}
