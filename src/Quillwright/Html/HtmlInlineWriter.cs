using System.Globalization;
using System.Text;
using Quillwright.Markdown;
using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Html;

/// <summary>
/// Turns the walker's tokens into inline HTML: semantic elements where HTML has them —
/// <c>strong</c>, <c>em</c>, <c>s</c>, <c>sub</c>, <c>sup</c>, <c>code</c>, <c>mark</c>,
/// <c>ins</c>, <c>del</c>, <c>a</c> — and CSS for what only Word can name: colour, size,
/// family, small caps, the shape of an underline.
/// </summary>
internal static class HtmlInlineWriter
{
    public static void Render(StringBuilder html, Paragraph paragraph, HtmlContext context)
    {
        List<MarkdownInlineToken> tokens = MarkdownInlineWalker.Walk(paragraph, context);
        int i = 0;
        while (i < tokens.Count)
        {
            Hyperlink? link = tokens[i].Link;
            if (link is null)
            {
                WriteToken(html, tokens[i++], context);
                continue;
            }

            int end = i;
            while (end < tokens.Count && ReferenceEquals(tokens[end].Link, link))
                end++;

            if (OpenLink(html, link, context))
            {
                for (; i < end; i++)
                    WriteToken(html, tokens[i], context);
                html.Append("</a>");
            }
            else
            {
                for (; i < end; i++)
                    WriteToken(html, tokens[i], context);
            }
        }
    }

    /// <summary>The text of a paragraph with no markup at all, for a title or an alt text.</summary>
    public static string Plain(Paragraph paragraph, HtmlContext context)
    {
        var text = new StringBuilder();
        foreach (MarkdownInlineToken token in MarkdownInlineWalker.Walk(paragraph, context))
        {
            switch (token.Kind)
            {
                case MarkdownInlineKind.Text:
                    text.Append(token.Text);
                    break;
                case MarkdownInlineKind.LineBreak or MarkdownInlineKind.BlockBreak or MarkdownInlineKind.Tab:
                    text.Append(' ');
                    break;
                default:
                    break;
            }
        }

        return text.ToString();
    }

    private static bool OpenLink(StringBuilder html, Hyperlink link, HtmlContext context)
    {
        string? href = null;
        if (link.Anchor is { Length: > 0 } anchor)
        {
            href = "#" + (context.Anchors.Resolve(anchor) ?? Slug(anchor));
        }
        else if (link.Url is { Length: > 0 } url)
        {
            if (!SafeUrl(url))
            {
                context.Diagnostics.Add(
                    HtmlExportWarningKind.UnsafeLinkSkipped,
                    "A hyperlink with a potentially executable target was rendered as plain text.",
                    Scheme(url));
                return false;
            }

            href = url;
        }

        if (href is null)
            return false;

        html.Append("<a href=\"").Append(Attribute(href)).Append('"');
        if (link.Tooltip is { Length: > 0 } tooltip)
            html.Append(" title=\"").Append(Attribute(tooltip)).Append('"');
        html.Append('>');
        return true;
    }

    private static void WriteToken(StringBuilder html, MarkdownInlineToken token, HtmlContext context)
    {
        switch (token.Kind)
        {
            case MarkdownInlineKind.Text:
                WriteText(html, token, context);
                break;

            case MarkdownInlineKind.LineBreak or MarkdownInlineKind.BlockBreak:
                html.Append("<br>\n");
                break;

            case MarkdownInlineKind.Tab:
                html.Append("<span style=\"display:inline-block;width:2em\"></span>");
                break;

            case MarkdownInlineKind.Anchor:
                html.Append("<a id=\"").Append(Attribute(token.Text ?? string.Empty)).Append("\"></a>");
                break;

            case MarkdownInlineKind.Picture when token.Picture is { } picture:
                WritePicture(html, picture, context);
                break;

            case MarkdownInlineKind.NoteReference when token.NoteReference is { } reference:
                if (context.Note(reference) is { } note)
                {
                    html.Append("<sup id=\"").Append(Attribute(note.Label)).Append("-ref\"><a href=\"#")
                        .Append(Attribute(note.Label)).Append("\">")
                        .Append(note.Number.ToString(CultureInfo.InvariantCulture))
                        .Append("</a></sup>");
                }

                break;

            default:
                break;
        }
    }

    private static void WritePicture(StringBuilder html, Picture picture, HtmlContext context)
    {
        html.Append("<img src=\"").Append(Attribute(context.ImageSource(picture.Image))).Append('"');
        html.Append(" alt=\"").Append(Attribute(picture.Description ?? picture.Name ?? string.Empty)).Append('"');

        if (picture.Width.Twips > 0 && picture.Height.Twips > 0)
        {
            html.Append(" style=\"width:").Append(Points(picture.Width))
                .Append("pt;height:").Append(Points(picture.Height)).Append("pt\"");
        }

        html.Append('>');
    }

    private static void WriteText(StringBuilder html, MarkdownInlineToken token, HtmlContext context)
    {
        if (string.IsNullOrEmpty(token.Text))
            return;

        var open = new List<string>();
        var close = new List<string>();

        void Wrap(string tag)
        {
            open.Add("<" + tag + ">");
            int space = tag.IndexOf(' ', StringComparison.Ordinal);
            close.Insert(0, "</" + (space > 0 ? tag[..space] : tag) + ">");
        }

        if (context.Options.RevisionMode == HtmlRevisionMode.Marked && token.Revision is { } revision)
        {
            Wrap(revision is RevisionKind.Deleted or RevisionKind.MovedFrom ? "del" : "ins");
        }

        RunFormat format = token.Resolved ?? RunFormat.Default;
        if (format.Bold == true)
            Wrap("strong");
        if (format.Italic == true)
            Wrap("em");
        if (format.Strike == true || format.DoubleStrike == true)
            Wrap("s");
        if (format.VerticalAlignment == VerticalTextAlignment.Superscript)
            Wrap("sup");
        else if (format.VerticalAlignment == VerticalTextAlignment.Subscript)
            Wrap("sub");
        if (MarkdownInlineWalker.IsMonospace(format))
            Wrap("code");

        if (format.Highlight is { } highlight && highlight != HighlightColor.None && HighlightHex(highlight) is { } shade)
            Wrap($"mark style=\"background:{shade}\"");
        if (format.Underline is { } underline && underline != UnderlineStyle.None)
        {
            Wrap(underline == UnderlineStyle.Single
                ? "u"
                : $"u style=\"text-decoration-style:{UnderlineCss(underline)}\"");
        }

        string css = Css(format, context);
        if (css.Length > 0)
            Wrap($"span style=\"{Attribute(css)}\"");

        foreach (string tag in open)
            html.Append(tag);
        html.Append(Escape(token.Text));
        foreach (string tag in close)
            html.Append(tag);
    }

    /// <summary>The CSS only Word could otherwise say: colour, size, family, case, shading.</summary>
    private static string Css(RunFormat format, HtmlContext context)
    {
        RunFormat baseline = context.Document.Styles.DefaultRunFormat;
        var css = new StringBuilder();

        if (format.Color is { IsAuto: false } color && context.Document.ResolveColor(color) is { } rgb)
            Append(css, "color", Hex(rgb));

        if (format.Shading is { IsEmpty: false } shading && shading.Fill is { IsAuto: false } fill &&
            context.Document.ResolveColor(fill) is { } background)
        {
            Append(css, "background", Hex(background));
        }

        if (format.Size is { } size && size != baseline.Size)
            Append(css, "font-size", Points(size) + "pt");

        if (format.FontAscii is { Length: > 0 } family && family != baseline.FontAscii &&
            !MarkdownInlineWalker.IsMonospace(format))
        {
            Append(css, "font-family", CssString(family));
        }

        if (format.SmallCaps == true)
            Append(css, "font-variant", "small-caps");
        else if (format.Caps == true)
            Append(css, "text-transform", "uppercase");

        return css.ToString();

        static void Append(StringBuilder css, string name, string value)
        {
            if (css.Length > 0)
                css.Append(';');
            css.Append(name).Append(':').Append(value);
        }
    }

    private static string UnderlineCss(UnderlineStyle underline) => underline switch
    {
        UnderlineStyle.Double or UnderlineStyle.WavyDouble => "double",
        UnderlineStyle.Dotted or UnderlineStyle.DottedHeavy => "dotted",
        UnderlineStyle.Dash or UnderlineStyle.DashedHeavy or UnderlineStyle.DashLong or UnderlineStyle.DashLongHeavy
            or UnderlineStyle.DotDash or UnderlineStyle.DashDotHeavy or UnderlineStyle.DotDotDash
            or UnderlineStyle.DashDotDotHeavy => "dashed",
        UnderlineStyle.Wave or UnderlineStyle.WavyHeavy => "wavy",
        _ => "solid",
    };

    private static string? HighlightHex(HighlightColor highlight) => highlight switch
    {
        HighlightColor.Black => "#000000",
        HighlightColor.Blue => "#0000ff",
        HighlightColor.Cyan => "#00ffff",
        HighlightColor.Green => "#00ff00",
        HighlightColor.Magenta => "#ff00ff",
        HighlightColor.Red => "#ff0000",
        HighlightColor.Yellow => "#ffff00",
        HighlightColor.White => "#ffffff",
        HighlightColor.DarkBlue => "#000080",
        HighlightColor.DarkCyan => "#008080",
        HighlightColor.DarkGreen => "#008000",
        HighlightColor.DarkMagenta => "#800080",
        HighlightColor.DarkRed => "#800000",
        HighlightColor.DarkYellow => "#808000",
        HighlightColor.DarkGray => "#808080",
        HighlightColor.LightGray => "#c0c0c0",
        _ => null,
    };

    private static string Hex(uint rgb) => "#" + (rgb & 0xFFFFFF).ToString("x6", CultureInfo.InvariantCulture);

    private static string Points(Length length) =>
        (length.Twips / 20.0).ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>Quotes a CSS string without changing its value.</summary>
    private static string CssString(string value)
    {
        var escaped = new StringBuilder(value.Length + 2).Append('\'');
        foreach (char character in value)
        {
            switch (character)
            {
                case '\\':
                    escaped.Append("\\\\");
                    break;
                case '\'':
                    escaped.Append("\\'");
                    break;
                case '\0':
                    escaped.Append("\\fffd ");
                    break;
                case '\n':
                    escaped.Append("\\a ");
                    break;
                case '\r':
                    escaped.Append("\\d ");
                    break;
                case '\f':
                    escaped.Append("\\c ");
                    break;
                default:
                    if (char.IsControl(character))
                        escaped.Append('\\').Append(((int)character).ToString("x", CultureInfo.InvariantCulture)).Append(' ');
                    else
                        escaped.Append(character);
                    break;
            }
        }

        return escaped.Append('\'').ToString();
    }

    private static bool SafeUrl(string url)
    {
        string trimmed = url.TrimStart();
        int colon = trimmed.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0)
            return true;

        int separator = trimmed.IndexOfAny(['/', '?', '#']);
        if (separator >= 0 && separator < colon)
            return true;

        string scheme = trimmed[..colon].ToLowerInvariant();
        return scheme is "http" or "https" or "mailto" or "tel" or "ftp" or "ftps" or "news";
    }

    private static string Scheme(string url)
    {
        int colon = url.IndexOf(':', StringComparison.Ordinal);
        return colon > 0 ? url[..colon].ToLowerInvariant() : "relative";
    }

    private static string Slug(string anchor)
    {
        var slug = new StringBuilder("bookmark");
        bool hyphen = false;
        foreach (char c in anchor)
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                slug.Append(char.ToLowerInvariant(c));
                hyphen = false;
            }
            else if (!hyphen)
            {
                slug.Append('-');
                hyphen = true;
            }
        }

        return slug.ToString().TrimEnd('-');
    }

    /// <summary>Escapes text content.</summary>
    public static string Escape(string text) => text
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal);

    /// <summary>Escapes an attribute value.</summary>
    public static string Attribute(string text) => Escape(text)
        .Replace("\"", "&quot;", StringComparison.Ordinal);
}
