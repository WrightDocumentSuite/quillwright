using System.Globalization;
using System.Text;
using Quillwright.Model;
using Quillwright.Rendering;
using Quillwright.Styles;

namespace Quillwright.Markdown;

internal static class MarkdownListWriter
{
    public static string Render(
        IList<Block> blocks,
        ref int index,
        MarkdownContext context)
    {
        var builder = new StringBuilder();
        int firstId = -1;
        int previousLevel = 0;
        int[] prefixes = new int[9];
        int[] childIndents = Enumerable.Repeat(4, 9).ToArray();

        while (index < blocks.Count && blocks[index] is Paragraph paragraph)
        {
            ParagraphFormat format = context.Resolver.ResolveParagraphFormat(paragraph);
            if (format.NumberingId is not { } id || id <= 0 ||
                context.Document.Numbering.ResolveLevel(id, format.NumberingLevel ?? 0) is null)
            {
                break;
            }

            if (firstId < 0)
                firstId = id;
            else if (id != firstId)
                break;

            int requestedLevel = Math.Clamp(format.NumberingLevel ?? 0, 0, 8);
            int level = requestedLevel;
            if (builder.Length == 0 && level > 0)
                level = 0;
            else if (level > previousLevel + 1)
                level = previousLevel + 1;

            if (level != requestedLevel)
            {
                context.Diagnostics.Add(
                    MarkdownExportWarningKind.StructureApproximated,
                    "A list depth jump is normalized to a valid Markdown nesting level.",
                    "list-depth");
            }

            if (level > previousLevel)
                prefixes[level] = prefixes[previousLevel] + childIndents[previousLevel];

            NumberLabel label = context.Lists.Next(format)!.Value;
            string marker = Marker(label, context);
            string content = MarkdownBlockWriter.RenderParagraph(paragraph, context, insideList: true);
            string prefix = new(' ', prefixes[level]);

            if (builder.Length > 0)
                builder.Append('\n');

            if (marker.Length == 0)
            {
                builder.Append(prefix).Append(content);
                context.Diagnostics.Add(
                    MarkdownExportWarningKind.StructureApproximated,
                    "A Word list level without a marker is represented as indented text.",
                    "markerless-list");
                childIndents[level] = 4;
            }
            else
            {
                builder.Append(prefix).Append(marker).Append(' ');
                int continuation = prefixes[level] + Math.Max(4, marker.Length + 1);
                builder.Append(IndentContinuation(content, continuation));
                childIndents[level] = Math.Max(4, marker.Length + 1);
            }

            previousLevel = level;
            index++;
        }

        return builder.ToString();
    }

    private static string Marker(NumberLabel label, MarkdownContext context)
    {
        if (label.Level.Format == ListNumberFormat.Bullet)
            return "-";
        if (label.Level.Format == ListNumberFormat.None)
            return string.Empty;

        if (label.Level.Format is not (ListNumberFormat.Decimal or ListNumberFormat.DecimalZero))
        {
            context.Diagnostics.Add(
                MarkdownExportWarningKind.StructureApproximated,
                "A non-decimal Word list marker is represented by Markdown's decimal ordered-list marker.",
                "list-format-" + label.Level.Format.ToString().ToLowerInvariant());
        }

        long safe = Math.Clamp((long)label.Value, 0, 999_999_999);
        return safe.ToString(CultureInfo.InvariantCulture) + ".";
    }

    private static string IndentContinuation(string content, int spaces)
    {
        string indentation = new(' ', spaces);
        return content.Replace("\n", "\n" + indentation, StringComparison.Ordinal);
    }
}
