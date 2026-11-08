using System.Globalization;
using System.Text;
using Quillwright.Model;
using Quillwright.Primitives;
using Quillwright.Styles;

namespace Quillwright.Rtf;

public static partial class RtfWriter
{
    private static void WriteNumberingTables(
        StringBuilder builder,
        WordDocument document,
        RtfExportDiagnostics diagnostics,
        ExportResources resources)
    {
        if (document.Numbering.Definitions.Count == 0 ||
            document.Numbering.Instances.Count == 0)
        {
            return;
        }

        builder.Append("{\\*\\listtable");
        foreach (AbstractNumbering abstractDef in document.Numbering.Definitions)
        {
            int listId = abstractDef.Id + 1000;
            builder.Append("{\\list\\listhybrid");
            foreach (NumberingLevel level in abstractDef.Levels.OrderBy(static l => l.Level))
            {
                int nfc = MapRtfNumberFormatCode(level.Format);
                int jc = level.Alignment switch
                {
                    ParagraphAlignment.Center => 1,
                    ParagraphAlignment.Right => 2,
                    _ => 0,
                };

                builder.Append("{\\listlevel")
                    .Append("\\levelnfc").Append(nfc.ToString(CultureInfo.InvariantCulture))
                    .Append("\\levelnfcn").Append(nfc.ToString(CultureInfo.InvariantCulture))
                    .Append("\\leveljc").Append(jc.ToString(CultureInfo.InvariantCulture))
                    .Append("\\leveljcn").Append(jc.ToString(CultureInfo.InvariantCulture))
                    .Append("\\levelstartat").Append(level.Start.ToString(CultureInfo.InvariantCulture));

                WriteLevelText(builder, level.Text, level.Format);
                WriteParagraphFormat(builder, level.ParagraphFormat, diagnostics, resources);
                WriteRunFormat(builder, level.RunFormat, diagnostics, resources);
                builder.Append('}');
            }

            builder.Append("\\listid").Append(listId.ToString(CultureInfo.InvariantCulture)).Append('}');
        }
        builder.Append('}');

        builder.Append("{\\*\\listoverridetable");
        foreach (NumberingInstance instance in document.Numbering.Instances)
        {
            int listId = instance.AbstractId + 1000;
            Dictionary<int, int> startOverrides = instance.Overrides
                .Where(static o => o.Level is >= 0 and <= 8 && o.StartOverride is not null)
                .GroupBy(static o => o.Level)
                .ToDictionary(static g => g.Key, static g => g.First().StartOverride!.Value);

            int overrideCount = startOverrides.Count == 0 ? 0 : startOverrides.Keys.Max() + 1;
            builder.Append("{\\listoverride\\listid")
                .Append(listId.ToString(CultureInfo.InvariantCulture))
                .Append("\\listoverridecount")
                .Append(overrideCount.ToString(CultureInfo.InvariantCulture));

            for (int level = 0; level < overrideCount; level++)
            {
                if (startOverrides.TryGetValue(level, out int startOverride))
                {
                    builder.Append("{\\lfolevel\\listoverridestartat\\levelstartat")
                        .Append(startOverride.ToString(CultureInfo.InvariantCulture))
                        .Append('}');
                }
                else
                {
                    builder.Append("{\\lfolevel}");
                }
            }

            builder.Append("\\ls")
                .Append(instance.Id.ToString(CultureInfo.InvariantCulture))
                .Append('}');
        }
        builder.Append('}');
    }

    private static void WriteLevelText(StringBuilder builder, string text, ListNumberFormat format)
    {
        var templateChars = new List<char>(text.Length);
        var placeholderPositions = new List<int>();
        for (int index = 0; index < text.Length; index++)
        {
            if (text[index] == '%' && index + 1 < text.Length && text[index + 1] is >= '1' and <= '9')
            {
                int levelIndex = text[index + 1] - '1';
                templateChars.Add((char)levelIndex);
                placeholderPositions.Add(templateChars.Count);
                index++;
                continue;
            }

            char ch = text[index];
            if (format == ListNumberFormat.Bullet && ch == '\uF0B7')
                ch = '\u2022';
            templateChars.Add(ch);
        }

        builder.Append("{\\leveltext\\'")
            .Append(templateChars.Count.ToString("x2", CultureInfo.InvariantCulture));
        foreach (char ch in templateChars)
        {
            if (ch <= 8)
            {
                builder.Append("\\'").Append(((int)ch).ToString("x2", CultureInfo.InvariantCulture));
            }
            else if (ch == ';')
            {
                builder.Append("\\'3b");
            }
            else
            {
                WriteCharacter(builder, ch);
            }
        }
        builder.Append(";}");

        builder.Append("{\\levelnumbers");
        foreach (int position in placeholderPositions)
        {
            builder.Append("\\'").Append(position.ToString("x2", CultureInfo.InvariantCulture));
        }
        builder.Append(";}");
    }

    private static int MapRtfNumberFormatCode(ListNumberFormat format) => format switch
    {
        ListNumberFormat.Decimal => 0,
        ListNumberFormat.UpperRoman => 1,
        ListNumberFormat.LowerRoman => 2,
        ListNumberFormat.UpperLetter => 3,
        ListNumberFormat.LowerLetter => 4,
        ListNumberFormat.Ordinal => 5,
        ListNumberFormat.DecimalZero => 22,
        ListNumberFormat.Bullet => 23,
        ListNumberFormat.None => 255,
        _ => 0,
    };

    private static void WriteSectionHeadersAndFooters(
        StringBuilder builder,
        Section section,
        RtfExportDiagnostics diagnostics,
        ExportResources resources,
        NoteExportContext notes)
    {
        if (section.Properties.DifferentFirstPage ||
            section.Headers.First is not null ||
            section.Footers.First is not null)
        {
            builder.Append("\\titlepg");
        }

        WriteHeaderFooterPart(builder, "header", section.Headers.Default, diagnostics, resources, notes);
        WriteHeaderFooterPart(builder, "headerl", section.Headers.Even, diagnostics, resources, notes);
        WriteHeaderFooterPart(builder, "headerf", section.Headers.First, diagnostics, resources, notes);
        WriteHeaderFooterPart(builder, "footer", section.Footers.Default, diagnostics, resources, notes);
        WriteHeaderFooterPart(builder, "footerl", section.Footers.Even, diagnostics, resources, notes);
        WriteHeaderFooterPart(builder, "footerf", section.Footers.First, diagnostics, resources, notes);
    }

    private static void WriteHeaderFooterPart(
        StringBuilder builder,
        string keyword,
        HeaderFooter? part,
        RtfExportDiagnostics diagnostics,
        ExportResources resources,
        NoteExportContext notes)
    {
        if (part is null || part.Blocks.Count == 0)
            return;

        builder.Append("{\\").Append(keyword).Append(' ');
        foreach (Block block in part.Blocks)
            WriteBlock(builder, block, diagnostics, resources, annotations: null, notes);
        builder.Append("}\n");
    }

    private static void WriteTable(
        StringBuilder builder,
        Table table,
        RtfExportDiagnostics diagnostics,
        ExportResources resources,
        AnnotationExportContext? annotations,
        NoteExportContext? notes)
    {
        int leftTwips = table.Format.Indent is { Unit: WidthUnit.Twips } indent
            ? indent.Value
            : 0;
        TableAlignment? tableAlignment = table.Format.Alignment;

        foreach (TableRow row in table.Rows)
        {
            builder.Append("\\trowd");
            if (leftTwips != 0)
                AppendControl(builder, "trleft", leftTwips);

            TableAlignment? rowAlignment = row.Format.Alignment ?? tableAlignment;
            if (rowAlignment is TableAlignment alignment)
            {
                builder.Append(alignment switch
                {
                    TableAlignment.Center => "\\trqc",
                    TableAlignment.Right => "\\trqr",
                    _ => "\\trql",
                });
            }

            if (row.Format.Height is Length height && height.Twips > 0)
            {
                int signedHeight = row.Format.HeightRule == HeightRule.Exact
                    ? -Math.Abs(height.Twips)
                    : Math.Abs(height.Twips);
                AppendControl(builder, "trrh", signedHeight);
            }

            if (row.Format.IsHeader == true)
                builder.Append("\\trhdr");
            if (row.Format.CannotSplit == true)
                builder.Append("\\trkeep");

            WriteRowBorders(builder, table.Format.Borders, diagnostics, resources);

            int currentBoundary = leftTwips;
            int gridColumn = 0;
            foreach (TableCell cell in row.Cells)
            {
                int span = Math.Max(1, cell.Format.GridSpan ?? 1);
                int totalCellTwips = ResolveCellTwips(table, cell, gridColumn, span);

                for (int slot = 0; slot < span; slot++)
                {
                    if (span > 1)
                        builder.Append(slot == 0 ? "\\clmgf" : "\\clmrg");

                    if (cell.Format.VerticalMerge == VerticalMerge.Restart)
                        builder.Append("\\clvmgf");
                    else if (cell.Format.VerticalMerge == VerticalMerge.Continue)
                        builder.Append("\\clvmrg");

                    if (cell.Format.VerticalAlignment is VerticalCellAlignment verticalAlignment)
                    {
                        builder.Append(verticalAlignment switch
                        {
                            VerticalCellAlignment.Center => "\\clvertalc",
                            VerticalCellAlignment.Bottom => "\\clvertalb",
                            _ => "\\clvertalt",
                        });
                    }

                    WriteCellBorders(builder, cell.Format.Borders, diagnostics, resources);

                    if (cell.Format.Shading?.Fill is WordColor fill && fill.Kind != ColorKind.Auto)
                        AppendControl(builder, "clcbpat", resources.ColorIndex(fill, diagnostics));

                    int slotTwips = ResolveSlotTwips(table, totalCellTwips, gridColumn, slot, span);
                    currentBoundary += Math.Max(1, slotTwips);
                    AppendControl(builder, "cellx", currentBoundary);
                }

                gridColumn += span;
            }

            builder.Append('\n');

            foreach (TableCell cell in row.Cells)
            {
                WriteCellContent(builder, cell, diagnostics, resources, annotations, notes);
                int span = Math.Max(1, cell.Format.GridSpan ?? 1);
                for (int extra = 1; extra < span; extra++)
                    builder.Append("\\pard\\intbl\\cell\n");
            }

            builder.Append("\\row\n");
        }
    }

    private static void WriteCellContent(
        StringBuilder builder,
        TableCell cell,
        RtfExportDiagnostics diagnostics,
        ExportResources resources,
        AnnotationExportContext? annotations,
        NoteExportContext? notes)
    {
        List<Paragraph> paragraphs = FlattenCellParagraphs(cell, diagnostics);
        if (paragraphs.Count == 0)
        {
            string fallback = cell.GetText();
            builder.Append("\\pard\\intbl ");
            if (fallback.Length > 0)
                WriteText(builder, fallback);
            builder.Append("\\cell\n");
            return;
        }

        for (int index = 0; index < paragraphs.Count; index++)
        {
            bool isLast = index == paragraphs.Count - 1;
            WriteParagraph(
                builder,
                paragraphs[index],
                diagnostics,
                resources,
                annotations,
                notes: notes,
                inTable: true,
                suppressTrailingPar: isLast);
            if (isLast)
                builder.Append("\\cell\n");
        }
    }

    private static int ResolveCellTwips(Table table, TableCell cell, int gridColumn, int span)
    {
        if (cell.Format.Width is { Unit: WidthUnit.Twips } cellWidth && cellWidth.Value > 0)
            return cellWidth.Value;

        int sum = 0;
        for (int offset = 0; offset < span; offset++)
        {
            int col = gridColumn + offset;
            sum += col < table.Grid.Count && table.Grid[col].Twips > 0
                ? table.Grid[col].Twips
                : 2160;
        }

        return Math.Max(1, sum);
    }

    private static int ResolveSlotTwips(Table table, int totalCellTwips, int gridColumn, int slot, int span)
    {
        if (span == 1)
            return totalCellTwips;

        int col = gridColumn + slot;
        if (col < table.Grid.Count && table.Grid[col].Twips > 0)
            return table.Grid[col].Twips;

        int baseSlot = totalCellTwips / span;
        return slot == span - 1 ? totalCellTwips - (baseSlot * (span - 1)) : baseSlot;
    }

    private static bool TryWriteFeatureObject(
        StringBuilder builder,
        InlineObject value,
        RtfExportDiagnostics diagnostics,
        ExportResources resources,
        NoteExportContext? notes)
    {
        switch (value)
        {
            case NoteReference noteReference:
                notes?.WriteNote(builder, noteReference, diagnostics, resources);
                return true;

            case NoteNumberMark:
                builder.Append("{\\chftn}");
                return true;

            case NoteSeparator separator:
                builder.Append(separator.IsContinuation ? "\\chftnsepc " : "\\chftnsep ");
                return true;

            case Picture picture:
                WritePicture(builder, picture, diagnostics);
                return true;

            default:
                return false;
        }
    }

    private static void WritePicture(
        StringBuilder builder,
        Picture picture,
        RtfExportDiagnostics diagnostics)
    {
        string? blipKeyword = picture.Image.ContentType switch
        {
            "image/png" => "\\pngblip",
            "image/jpeg" => "\\jpegblip",
            _ => picture.Image.Extension switch
            {
                "png" => "\\pngblip",
                "jpg" or "jpeg" => "\\jpegblip",
                _ => null,
            },
        };

        if (blipKeyword is null)
        {
            diagnostics.Add(
                RtfExportWarningKind.UnsupportedInline,
                $"Picture format '{picture.Image.ContentType}' is not supported by the RTF exporter.",
                nameof(Picture));
            return;
        }

        int widthTwips = Math.Max(1, picture.Width.Twips);
        int heightTwips = Math.Max(1, picture.Height.Twips);
        builder.Append("{\\pict");
        WritePictureProperties(builder, picture);
        builder.Append(blipKeyword)
            .Append("\\picwgoal").Append(widthTwips.ToString(CultureInfo.InvariantCulture))
            .Append("\\pichgoal").Append(heightTwips.ToString(CultureInfo.InvariantCulture))
            .Append(' ');

        ReadOnlySpan<byte> bytes = picture.Image.Bytes.Span;
        for (int index = 0; index < bytes.Length; index++)
        {
            builder.Append(bytes[index].ToString("x2", CultureInfo.InvariantCulture));
        }

        builder.Append('}');
    }

    /// <summary>Word reads an inline picture's name and alternative text from its shape properties.</summary>
    private static void WritePictureProperties(StringBuilder builder, Picture picture)
    {
        if (string.IsNullOrEmpty(picture.Name) && string.IsNullOrEmpty(picture.Description))
            return;

        builder.Append("{\\*\\picprop");
        WriteShapeProperty(builder, "wzName", picture.Name);
        WriteShapeProperty(builder, "wzDescription", picture.Description);
        builder.Append('}');
    }

    private static void WriteShapeProperty(StringBuilder builder, string name, string? value)
    {
        if (string.IsNullOrEmpty(value))
            return;

        builder.Append("{\\sp{\\sn ").Append(name).Append("}{\\sv ");
        foreach (char character in value)
        {
            // A control character is part of the value; \line or \tab would be body content.
            if (character < ' ')
                builder.Append("\\u").Append(((int)character).ToString(CultureInfo.InvariantCulture)).Append('?');
            else
                WriteCharacter(builder, character);
        }

        builder.Append("}}");
    }

    private sealed class NoteExportContext
    {
        private readonly Dictionary<int, Note> _footnotes = [];
        private readonly Dictionary<int, Note> _endnotes = [];

        public NoteExportContext(WordDocument document)
        {
            foreach (Note note in document.Footnotes)
            {
                if (note.Kind == NoteKind.Normal)
                    _footnotes.TryAdd(note.Id, note);
            }

            foreach (Note note in document.Endnotes)
            {
                if (note.Kind == NoteKind.Normal)
                    _endnotes.TryAdd(note.Id, note);
            }
        }

        public void WriteNote(
            StringBuilder builder,
            NoteReference reference,
            RtfExportDiagnostics diagnostics,
            ExportResources resources)
        {
            Dictionary<int, Note> lookup = reference.IsEndnote ? _endnotes : _footnotes;
            if (!lookup.TryGetValue(reference.Id, out Note? note))
            {
                diagnostics.Add(
                    RtfExportWarningKind.ContentSkipped,
                    $"A {(reference.IsEndnote ? "endnote" : "footnote")} reference points to missing note {reference.Id} and was omitted.",
                    "orphan-note-reference");
                return;
            }

            builder.Append("{\\super\\chftn}{\\footnote");
            if (reference.IsEndnote)
                builder.Append("\\ftnalt");
            builder.Append(' ');

            Paragraph[] paragraphs = [.. note.Blocks.OfType<Paragraph>()];
            for (int index = 0; index < paragraphs.Length; index++)
            {
                WriteParagraph(
                    builder,
                    paragraphs[index],
                    diagnostics,
                    resources,
                    annotations: null,
                    notes: this,
                    suppressTrailingPar: index == paragraphs.Length - 1,
                    insideNote: true);
            }

            builder.Append('}');
        }
    }
}
