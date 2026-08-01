using Quillwright.Model;

namespace Quillwright.Markdown;

internal static class MarkdownRevisionView
{
    public static bool JoinsNext(Paragraph paragraph, MarkdownRevisionMode mode)
    {
        string? xml = paragraph.MarkFormat.MarkRevisionXml;
        return xml is not null && mode switch
        {
            MarkdownRevisionMode.Accepted => xml.Contains("<w:del", StringComparison.Ordinal),
            MarkdownRevisionMode.Original => xml.Contains("<w:ins", StringComparison.Ordinal),
            _ => false,
        };
    }

    public static bool RowVisible(TableRow row, MarkdownRevisionMode mode) => mode switch
    {
        MarkdownRevisionMode.Accepted => row.Format.DeletedXml is null,
        MarkdownRevisionMode.Original => row.Format.InsertedXml is null,
        _ => true,
    };
}
