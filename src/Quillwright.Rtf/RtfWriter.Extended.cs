using System.Globalization;
using System.Text;
using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Rtf;

public static partial class RtfWriter
{
    private static void WriteStyleSheet(
        StringBuilder builder,
        RtfExportDiagnostics diagnostics,
        ExportResources resources)
    {
        if (!resources.HasStylesToExport)
            return;

        builder.Append("{\\stylesheet");
        foreach ((Style style, int index) in resources.OrderedStyles)
        {
            if (style.Kind == StyleKind.Paragraph)
            {
                builder.Append("{\\s").Append(index.ToString(CultureInfo.InvariantCulture));
                WriteParagraphFormat(builder, style.ParagraphFormat with { StyleId = null }, diagnostics, resources);
                WriteRunFormat(builder, style.RunFormat with { StyleId = null }, diagnostics, resources);
            }
            else if (style.Kind == StyleKind.Character)
            {
                builder.Append("{\\*\\cs").Append(index.ToString(CultureInfo.InvariantCulture));
                WriteRunFormat(builder, style.RunFormat with { StyleId = null }, diagnostics, resources);
            }
            else if (style.Kind == StyleKind.Table)
            {
                builder.Append("{\\*\\ts").Append(index.ToString(CultureInfo.InvariantCulture));
            }
            else
            {
                continue;
            }

            if (style.BasedOn is { Length: > 0 } basedOn && resources.TryGetStyleIndex(basedOn, out int basedOnIdx))
                AppendControl(builder, "sbasedon", basedOnIdx);
            if (style.NextStyle is { Length: > 0 } nextStyle && resources.TryGetStyleIndex(nextStyle, out int nextIdx))
                AppendControl(builder, "snext", nextIdx);
            if (style.LinkedStyle is { Length: > 0 } linkedStyle && resources.TryGetStyleIndex(linkedStyle, out int linkIdx))
                AppendControl(builder, "slink", linkIdx);
            if (style.Priority is int priority)
                AppendControl(builder, "spriority", priority);
            if (style.QuickFormat)
                builder.Append("\\sqformat");
            if (style.Hidden)
                builder.Append("\\shidden");
            if (style.SemiHidden)
                builder.Append("\\ssemihidden");
            if (style.UnhideWhenUsed)
                builder.Append("\\sunhideused");

            builder.Append(' ');
            string displayName = string.IsNullOrWhiteSpace(style.Name) ? style.Id : style.Name;
            foreach (char ch in displayName)
                WriteCharacter(builder, ch == ';' ? ',' : ch);
            builder.Append(";}");
        }

        builder.Append('}');
    }

    private static void WriteDocumentInfo(StringBuilder builder, DocumentProperties properties)
    {
        bool hasRevision = int.TryParse(properties.Revision, NumberStyles.Integer, CultureInfo.InvariantCulture, out int revision);
        if (string.IsNullOrEmpty(properties.Title) &&
            string.IsNullOrEmpty(properties.Subject) &&
            string.IsNullOrEmpty(properties.Creator) &&
            string.IsNullOrEmpty(properties.Keywords) &&
            string.IsNullOrEmpty(properties.Description) &&
            string.IsNullOrEmpty(properties.LastModifiedBy) &&
            string.IsNullOrEmpty(properties.Category) &&
            !hasRevision &&
            properties.Created is null &&
            properties.Modified is null)
        {
            return;
        }

        builder.Append("{\\info");
        WriteInfoString(builder, "title", properties.Title, starred: false);
        WriteInfoString(builder, "subject", properties.Subject, starred: false);
        WriteInfoString(builder, "author", properties.Creator, starred: false);
        WriteInfoString(builder, "keywords", properties.Keywords, starred: false);
        WriteInfoString(builder, "doccomm", properties.Description, starred: false);
        WriteInfoString(builder, "operator", properties.LastModifiedBy, starred: false);
        WriteInfoString(builder, "category", properties.Category, starred: true);
        if (hasRevision)
            builder.Append("{\\version").Append(revision.ToString(CultureInfo.InvariantCulture)).Append('}');
        if (properties.Created is DateTimeOffset created)
            WriteInfoDate(builder, "creatim", created);
        if (properties.Modified is DateTimeOffset modified)
            WriteInfoDate(builder, "revtim", modified);
        builder.Append('}');
    }

    private static void WriteInfoString(StringBuilder builder, string keyword, string? value, bool starred)
    {
        if (string.IsNullOrEmpty(value))
            return;

        builder.Append(starred ? "{\\*\\" : "{\\").Append(keyword).Append(' ');
        WriteText(builder, value);
        builder.Append('}');
    }

    private static void WriteInfoDate(StringBuilder builder, string keyword, DateTimeOffset value)
    {
        DateTime utc = value.UtcDateTime;
        builder.Append("{\\").Append(keyword)
            .Append("\\yr").Append(utc.Year.ToString(CultureInfo.InvariantCulture))
            .Append("\\mo").Append(utc.Month.ToString(CultureInfo.InvariantCulture))
            .Append("\\dy").Append(utc.Day.ToString(CultureInfo.InvariantCulture))
            .Append("\\hr").Append(utc.Hour.ToString(CultureInfo.InvariantCulture))
            .Append("\\min").Append(utc.Minute.ToString(CultureInfo.InvariantCulture))
            .Append("\\sec").Append(utc.Second.ToString(CultureInfo.InvariantCulture))
            .Append('}');
    }

    private static void WriteParagraphBorders(
        StringBuilder builder,
        BorderSet? borders,
        RtfExportDiagnostics diagnostics,
        ExportResources resources)
    {
        if (borders is null || borders.IsEmpty)
            return;

        WriteBorderEdge(builder, "brdrt", borders.Top, diagnostics, resources);
        WriteBorderEdge(builder, "brdrl", borders.Left, diagnostics, resources);
        WriteBorderEdge(builder, "brdrb", borders.Bottom, diagnostics, resources);
        WriteBorderEdge(builder, "brdrr", borders.Right, diagnostics, resources);
        WriteBorderEdge(builder, "brdrbtw", borders.InsideHorizontal, diagnostics, resources);
        WriteBorderEdge(builder, "brdrbar", borders.Bar, diagnostics, resources);
    }

    private static void WriteSectionProperties(StringBuilder builder, SectionProperties properties)
    {
        bool wroteAny = false;

        if (properties.Start != SectionStart.NextPage)
        {
            string? breakKeyword = properties.Start switch
            {
                SectionStart.Continuous => "\\sbknone",
                SectionStart.NextColumn => "\\sbkcol",
                SectionStart.EvenPage => "\\sbkeven",
                SectionStart.OddPage => "\\sbkodd",
                _ => null,
            };
            if (breakKeyword is not null)
            {
                builder.Append(breakKeyword);
                wroteAny = true;
            }
        }

        if (properties.Orientation == PageOrientation.Landscape)
        {
            builder.Append("\\lndscpsxn");
            wroteAny = true;
        }

        int defaultWidthTwips = Length.FromMillimeters(210).Twips;
        int defaultHeightTwips = Length.FromMillimeters(297).Twips;
        if (properties.PageWidth.Twips != defaultWidthTwips ||
            properties.PageHeight.Twips != defaultHeightTwips ||
            properties.Orientation == PageOrientation.Landscape)
        {
            AppendControl(builder, "pgwsxn", properties.PageWidth.Twips);
            AppendControl(builder, "pghsxn", properties.PageHeight.Twips);
            wroteAny = true;
        }

        int oneInchTwips = Length.FromInches(1).Twips;
        int halfInchTwips = Length.FromInches(0.5).Twips;
        PageMargins margins = properties.Margins;
        if (margins.Left.Twips != oneInchTwips)
        {
            AppendControl(builder, "marglsxn", margins.Left.Twips);
            wroteAny = true;
        }
        if (margins.Right.Twips != oneInchTwips)
        {
            AppendControl(builder, "margrsxn", margins.Right.Twips);
            wroteAny = true;
        }
        if (margins.Top.Twips != oneInchTwips)
        {
            AppendControl(builder, "margtsxn", margins.Top.Twips);
            wroteAny = true;
        }
        if (margins.Bottom.Twips != oneInchTwips)
        {
            AppendControl(builder, "margbsxn", margins.Bottom.Twips);
            wroteAny = true;
        }
        if (margins.Gutter.Twips != 0)
        {
            AppendControl(builder, "guttersxn", margins.Gutter.Twips);
            wroteAny = true;
        }
        if (margins.Header.Twips != halfInchTwips)
        {
            AppendControl(builder, "headery", margins.Header.Twips);
            wroteAny = true;
        }
        if (margins.Footer.Twips != halfInchTwips)
        {
            AppendControl(builder, "footery", margins.Footer.Twips);
            wroteAny = true;
        }

        ColumnLayout columns = properties.Columns;
        if (columns.Count > 1 || !columns.EqualWidth || columns.Columns.Count > 0 || columns.Separator)
        {
            int count = Math.Max(1, columns.Columns.Count > 0 ? Math.Max(columns.Count, columns.Columns.Count) : columns.Count);
            AppendControl(builder, "cols", count);
            if (columns.Space.Twips != halfInchTwips)
                AppendControl(builder, "colsx", columns.Space.Twips);
            if (columns.Separator)
                builder.Append("\\linebetcol");
            if (!columns.EqualWidth && columns.Columns.Count > 0)
            {
                for (int i = 0; i < columns.Columns.Count; i++)
                {
                    AppendControl(builder, "colno", i + 1);
                    AppendControl(builder, "colw", columns.Columns[i].Width.Twips);
                    if (columns.Columns[i].Space.Twips > 0 || i < columns.Columns.Count - 1)
                        AppendControl(builder, "colsr", columns.Columns[i].Space.Twips);
                }
            }
            wroteAny = true;
        }

        if (wroteAny)
            builder.Append('\n');
    }

    private static void WriteRowBorders(
        StringBuilder builder,
        BorderSet? borders,
        RtfExportDiagnostics diagnostics,
        ExportResources resources)
    {
        if (borders is null || borders.IsEmpty)
            return;

        WriteBorderEdge(builder, "trbrdrt", borders.Top, diagnostics, resources);
        WriteBorderEdge(builder, "trbrdrl", borders.Left, diagnostics, resources);
        WriteBorderEdge(builder, "trbrdrb", borders.Bottom, diagnostics, resources);
        WriteBorderEdge(builder, "trbrdrr", borders.Right, diagnostics, resources);
        WriteBorderEdge(builder, "trbrdrh", borders.InsideHorizontal, diagnostics, resources);
        WriteBorderEdge(builder, "trbrdrv", borders.InsideVertical, diagnostics, resources);
    }

    private static void WriteCellBorders(
        StringBuilder builder,
        BorderSet? borders,
        RtfExportDiagnostics diagnostics,
        ExportResources resources)
    {
        if (borders is null || borders.IsEmpty)
            return;

        WriteBorderEdge(builder, "clbrdrt", borders.Top, diagnostics, resources);
        WriteBorderEdge(builder, "clbrdrl", borders.Left, diagnostics, resources);
        WriteBorderEdge(builder, "clbrdrb", borders.Bottom, diagnostics, resources);
        WriteBorderEdge(builder, "clbrdrr", borders.Right, diagnostics, resources);
    }

    private static void WriteBorderEdge(
        StringBuilder builder,
        string selector,
        BorderLine? edge,
        RtfExportDiagnostics diagnostics,
        ExportResources resources)
    {
        if (edge is null || edge.Style == BorderStyle.Nil)
            return;

        builder.Append('\\').Append(selector);
        if (edge.Style == BorderStyle.None)
        {
            builder.Append("\\brdrnone");
            return;
        }

        string styleWord = edge.Style switch
        {
            BorderStyle.Single => "\\brdrs",
            BorderStyle.Thick => "\\brdrth",
            BorderStyle.Double => "\\brdrdb",
            BorderStyle.Dotted => "\\brdrdot",
            BorderStyle.Dashed => "\\brdrdash",
            BorderStyle.DashSmallGap => "\\brdrdashsm",
            BorderStyle.DotDash => "\\brdrdashd",
            BorderStyle.DotDotDash => "\\brdrdashdd",
            BorderStyle.Triple => "\\brdrtriple",
            BorderStyle.Wave or BorderStyle.DoubleWave => "\\brdrwavy",
            _ => "\\brdrs",
        };
        builder.Append(styleWord);

        int widthTwips = edge.Width.Twips > 0 ? edge.Width.Twips : 15;
        AppendControl(builder, "brdrw", widthTwips);

        if (!edge.Color.IsAuto)
        {
            int colorIndex = resources.ColorIndex(edge.Color, diagnostics);
            if (colorIndex > 0)
                AppendControl(builder, "brdrcf", colorIndex);
        }

        if (edge.Space.Twips > 0)
            AppendControl(builder, "brsp", edge.Space.Twips);
    }

    private static void WriteHyperlinkStart(StringBuilder builder, Hyperlink hyperlink)
    {
        builder.Append("{\\field{\\*\\fldinst HYPERLINK");
        if (!string.IsNullOrEmpty(hyperlink.Url))
        {
            builder.Append(" \"");
            WriteFieldInstructionString(builder, hyperlink.Url);
            builder.Append('"');
        }

        if (!string.IsNullOrEmpty(hyperlink.Anchor))
        {
            builder.Append(" \\\\l \"");
            WriteFieldInstructionString(builder, hyperlink.Anchor);
            builder.Append('"');
        }

        if (!string.IsNullOrEmpty(hyperlink.Tooltip))
        {
            builder.Append(" \\\\o \"");
            WriteFieldInstructionString(builder, hyperlink.Tooltip);
            builder.Append('"');
        }

        builder.Append("}{\\fldrslt ");
    }

    private static void WriteFieldInstructionString(StringBuilder builder, string value)
    {
        foreach (char ch in value)
        {
            if (ch == '"')
            {
                builder.Append("\\\\\"");
                continue;
            }

            WriteCharacter(builder, ch);
        }
    }

    private static List<(int Start, int End)> SplitRunSegments(
        int runStart,
        int runEnd,
        IReadOnlyList<(int Start, int End, Hyperlink Link)> hyperlinks)
    {
        if (hyperlinks.Count == 0 || runStart >= runEnd)
            return [(runStart, runEnd)];

        var boundaries = new SortedSet<int> { runStart, runEnd };
        foreach ((int start, int end, _) in hyperlinks)
        {
            if (start > runStart && start < runEnd)
                boundaries.Add(start);
            if (end > runStart && end < runEnd)
                boundaries.Add(end);
        }

        var segments = new List<(int Start, int End)>(boundaries.Count - 1);
        int? previous = null;
        foreach (int boundary in boundaries)
        {
            if (previous is int prev && boundary > prev)
                segments.Add((prev, boundary));
            previous = boundary;
        }

        return segments;
    }

    private static List<Paragraph> FlattenCellParagraphs(TableCell cell, RtfExportDiagnostics diagnostics)
    {
        var result = new List<Paragraph>(cell.Blocks.Count);
        FlattenBlocksIntoCellParagraphs(cell.Blocks, result, diagnostics);
        return result;
    }

    private static void FlattenBlocksIntoCellParagraphs(
        IEnumerable<Block> blocks,
        List<Paragraph> result,
        RtfExportDiagnostics diagnostics)
    {
        foreach (Block block in blocks)
        {
            if (block is Paragraph paragraph)
            {
                result.Add(paragraph);
                continue;
            }

            diagnostics.Add(
                RtfExportWarningKind.UnsupportedBlock,
                $"{block.GetType().Name} in table cell is flattened to paragraphs.",
                $"cell-{block.GetType().Name}");

            if (block is Table nestedTable)
            {
                foreach (TableRow row in nestedTable.Rows)
                {
                    foreach (TableCell nestedCell in row.Cells)
                        FlattenBlocksIntoCellParagraphs(nestedCell.Blocks, result, diagnostics);
                }
                continue;
            }

            string text = block.GetText();
            foreach (string line in text.Split('\n'))
                result.Add(new Paragraph(line));
        }
    }
}
