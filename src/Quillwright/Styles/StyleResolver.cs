using Quillwright.Model;

namespace Quillwright.Styles;

/// <summary>
/// Works out the formatting that actually applies to a paragraph or a run, after the whole
/// inheritance chain has had its say.
/// </summary>
/// <remarks>
/// <para>
/// ISO-29500 §17.7.2 layers formatting in a fixed order: document defaults, then the table
/// style of the table the content sits in, then the numbering definition, then the paragraph
/// style chain from its root down, then the character style, and finally the direct
/// formatting on the element itself. The numbering layer contributes paragraph properties
/// only: the level's own <c>w:rPr</c> dresses the bullet or number, not the text after it
/// (§17.9.24), and is read through <see cref="ResolveNumberingSymbolFormat"/>.
/// </para>
/// <para>
/// Bold, italic and the other toggles do not simply overwrite across those layers — they
/// exclusive-or (§17.7.3), which is why a bold character style over a bold paragraph style
/// comes out unbold. Two exceptions: direct formatting always means what it says, and inside
/// one layer a <c>basedOn</c> chain is a single value rather than a series to combine — the
/// most derived style that states the property wins. Both are
/// <see cref="RunFormat.Apply"/>; the exclusive-or is <see cref="RunFormat.Merge"/>.
/// </para>
/// <para>
/// One deliberate departure from §17.7.3: a toggle the document defaults turn on is said to
/// settle the matter, and here it takes part in the exclusive-or like any other layer. That
/// is what Word does ([MS-OI29500] 2.1.230(a)), and matching Word is what keeps a document
/// looking the way its author saw it.
/// </para>
/// </remarks>
public sealed class StyleResolver
{
    private readonly WordDocument _document;
    private readonly Dictionary<string, RunFormat> _runByStyle = [];
    private readonly Dictionary<string, ParagraphFormat> _paragraphByStyle = [];
    private int _cachedVersion = -1;

    internal StyleResolver(WordDocument document) => _document = document;

    /// <summary>The paragraph formatting in force, including everything inherited.</summary>
    /// <param name="paragraph">The paragraph to resolve.</param>
    public ParagraphFormat ResolveParagraphFormat(Paragraph paragraph) =>
        ResolveParagraphFormatCore(paragraph, numbering: null);

    internal ParagraphFormat ResolveParagraphFormat(Paragraph paragraph, NumberingResolver numbering)
    {
        ArgumentNullException.ThrowIfNull(numbering);
        return ResolveParagraphFormatCore(paragraph, numbering);
    }

    private ParagraphFormat ResolveParagraphFormatCore(
        Paragraph paragraph,
        NumberingResolver? numbering)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        Refresh();

        ParagraphFormat result = _document.Styles.DefaultParagraphFormat;
        result = result.MergeForStyleHierarchy(TableContribution(paragraph).Paragraph);
        result = result.MergeForStyleHierarchy(NumberingContribution(paragraph, numbering).Paragraph);
        result = result.MergeForStyleHierarchy(ParagraphStyleFormat(paragraph.Format.StyleId));
        return result.MergeForStyleHierarchy(paragraph.Format);
    }

    /// <summary>The character formatting in force on a run, including everything inherited.</summary>
    /// <param name="run">The run to resolve.</param>
    public RunFormat ResolveRunFormat(Run run) => ResolveRunFormat(run.Paragraph, run.Format);

    /// <summary>The character formatting in force on the paragraph mark.</summary>
    /// <param name="paragraph">The paragraph whose mark to resolve.</param>
    public RunFormat ResolveMarkFormat(Paragraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        return ResolveRunFormat(paragraph, paragraph.MarkFormat);
    }

    /// <summary>The character formatting in force for a given direct format inside a paragraph.</summary>
    /// <param name="paragraph">The paragraph the run belongs to.</param>
    /// <param name="direct">Direct formatting on the run.</param>
    public RunFormat ResolveRunFormat(Paragraph paragraph, RunFormat direct)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        ArgumentNullException.ThrowIfNull(direct);
        Refresh();

        RunFormat result = _document.Styles.DefaultRunFormat;
        result = result.Merge(TableContribution(paragraph).Run);
        result = result.Merge(ParagraphStyleRunFormat(paragraph.Format.StyleId));
        result = result.Merge(CharacterStyleFormat(direct.StyleId));
        return result.Apply(direct);
    }

    /// <summary>The table formatting in force after its table-style chain and direct format.</summary>
    public TableFormat ResolveTableFormat(Table table)
    {
        ArgumentNullException.ThrowIfNull(table);
        Refresh();

        TableFormat result = TableFormat.Default;
        foreach (Style style in _document.Styles.Chain(table.Format.StyleId))
            result = Apply(result, style.TableFormat);

        return Apply(result, table.Format);
    }

    /// <summary>The row formatting in force after its table style and direct format.</summary>
    public TableRowFormat ResolveTableRowFormat(TableRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.Table is not { } table)
            return row.Format;

        Refresh();
        TableFormat tableFormat = ResolveTableFormat(table);
        TableRowFormat result = TableRowFormat.Default;

        foreach (Style style in _document.Styles.Chain(table.Format.StyleId))
        {
            result = Apply(result, style.RowFormat);
            if (row.Cells.Count == 0)
                continue;

            foreach (TableStyleRegion region in TableRegions.For(table, row.Cells[0], tableFormat))
            {
                if (region is not (TableStyleRegion.Band1Horizontal or TableStyleRegion.Band2Horizontal or
                    TableStyleRegion.FirstRow or TableStyleRegion.LastRow))
                    continue;

                ConditionalTableStyle? conditional = style.ConditionalFormats.FirstOrDefault(c => c.Region == region);
                if (conditional is not null)
                    result = Apply(result, conditional.RowFormat);
            }
        }

        return Apply(result, row.Format);
    }

    /// <summary>The cell formatting in force after table and conditional styles and direct format.</summary>
    public TableCellFormat ResolveTableCellFormat(TableCell cell)
    {
        ArgumentNullException.ThrowIfNull(cell);
        if (cell.Row?.Table is not { } table)
            return cell.Format;

        Refresh();
        TableFormat tableFormat = ResolveTableFormat(table);
        TableCellFormat result = TableCellFormat.Default;

        foreach (Style style in _document.Styles.Chain(table.Format.StyleId))
        {
            result = Apply(result, style.CellFormat);
            foreach (TableStyleRegion region in TableRegions.For(table, cell, tableFormat))
            {
                ConditionalTableStyle? conditional = style.ConditionalFormats.FirstOrDefault(c => c.Region == region);
                if (conditional is not null)
                    result = Apply(result, conditional.CellFormat);
            }
        }

        return Apply(result, cell.Format);
    }

    /// <summary>
    /// The character formatting of the numbering symbol a paragraph is preceded by, or
    /// <see langword="null"/> when the paragraph is not in a list.
    /// </summary>
    /// <param name="paragraph">The paragraph whose marker to resolve.</param>
    /// <remarks>
    /// The bullet or number is not part of the text, and its appearance does not come from the
    /// text either: it is the formatting of the paragraph mark with the level's own
    /// <c>w:rPr</c> laid over it (ISO/IEC 29500-1 §17.9.24). That is why a bulleted paragraph is
    /// set in the body font while its bullet comes from Symbol.
    /// </remarks>
    public RunFormat? ResolveNumberingSymbolFormat(Paragraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        Refresh();

        RunFormat contribution = NumberingContribution(paragraph).Run;
        return contribution.IsEmpty && NumberingReference(paragraph).Id is null
            ? null
            : ResolveMarkFormat(paragraph).Apply(contribution);
    }

    /// <summary>Drops everything cached; call after editing style definitions in place.</summary>
    public void Invalidate() => _cachedVersion = -1;

    private void Refresh()
    {
        if (_cachedVersion == _document.Styles.Version)
            return;

        _runByStyle.Clear();
        _paragraphByStyle.Clear();
        _cachedVersion = _document.Styles.Version;
    }

    private ParagraphFormat ParagraphStyleFormat(string? styleId)
    {
        if (styleId is null)
            return ParagraphFormat.Default;
        if (_paragraphByStyle.TryGetValue(styleId, out ParagraphFormat? cached))
            return cached;

        var result = ParagraphFormat.Default;
        foreach (Style style in _document.Styles.Chain(styleId))
            result = result.MergeForStyleHierarchy(style.ParagraphFormat);

        _paragraphByStyle[styleId] = result;
        return result;
    }

    private RunFormat ParagraphStyleRunFormat(string? styleId) => ChainRunFormat(styleId, "p:");

    private RunFormat CharacterStyleFormat(string? styleId) => ChainRunFormat(styleId, "c:");

    private RunFormat ChainRunFormat(string? styleId, string cachePrefix)
    {
        if (styleId is null)
            return RunFormat.Default;

        string key = cachePrefix + styleId;
        if (_runByStyle.TryGetValue(key, out RunFormat? cached))
            return cached;

        // The chain is one layer of the hierarchy, so a toggle it states more than once is
        // not exclusive-ored: the value nearest the style asked for is the one that counts.
        var result = RunFormat.Default;
        foreach (Style style in _document.Styles.Chain(styleId))
            result = result.Apply(style.RunFormat);

        _runByStyle[key] = result;
        return result;
    }

    private (ParagraphFormat Paragraph, RunFormat Run) NumberingContribution(
        Paragraph paragraph,
        NumberingResolver? numbering = null)
    {
        (int? numberingId, int level) = NumberingReference(paragraph);
        if (numberingId is not { } id)
            return (ParagraphFormat.Default, RunFormat.Default);

        NumberingLevel? definition = numbering is null
            ? _document.Numbering.ResolveLevel(id, level)
            : numbering.ResolveLevel(id, level);
        return definition is null
            ? (ParagraphFormat.Default, RunFormat.Default)
            : (definition.ParagraphFormat, definition.RunFormat);
    }

    /// <summary>The list a paragraph is in, wherever the reference to it came from.</summary>
    /// <remarks>
    /// §17.7.2 puts numbering before the paragraph style in the order of application, but the
    /// <c>w:numPr</c> that names the list is just as often <em>in</em> that style — a numbered
    /// heading carries it there rather than on every paragraph. Reading only the direct
    /// formatting would leave such a paragraph with no numbering layer at all: no indents from
    /// its level, and no <c>w:rPr</c> for the marker.
    /// </remarks>
    private (int? Id, int Level) NumberingReference(Paragraph paragraph)
    {
        ParagraphFormat direct = paragraph.Format;
        if (direct.NumberingId is { } stated)
            return (stated, direct.NumberingLevel ?? 0);

        ParagraphFormat fromStyle = ParagraphStyleFormat(direct.StyleId);
        return (fromStyle.NumberingId, direct.NumberingLevel ?? fromStyle.NumberingLevel ?? 0);
    }

    private (ParagraphFormat Paragraph, RunFormat Run) TableContribution(Paragraph paragraph)
    {
        if (paragraph.Parent is not TableCell cell || cell.Row?.Table is not { } table)
            return (ParagraphFormat.Default, RunFormat.Default);

        var paragraphResult = ParagraphFormat.Default;
        var runResult = RunFormat.Default;

        // A table style and the conditional formats inside it are all one layer, so the
        // narrowest statement of a property wins rather than combining with the wider ones.
        TableFormat tableFormat = ResolveTableFormat(table);
        foreach (Style style in _document.Styles.Chain(table.Format.StyleId))
        {
            paragraphResult = paragraphResult.MergeForStyleHierarchy(style.ParagraphFormat);
            runResult = runResult.Apply(style.RunFormat);

            foreach (TableStyleRegion region in TableRegions.For(table, cell, tableFormat))
            {
                ConditionalTableStyle? conditional = style.ConditionalFormats.FirstOrDefault(c => c.Region == region);
                if (conditional is null)
                    continue;
                paragraphResult = paragraphResult.MergeForStyleHierarchy(conditional.ParagraphFormat);
                runResult = runResult.Apply(conditional.RunFormat);
            }
        }

        return (paragraphResult, runResult);
    }

    private static TableFormat Apply(TableFormat current, TableFormat over) => current with
    {
        StyleId = over.StyleId ?? current.StyleId,
        FloatingPositionXml = over.FloatingPositionXml ?? current.FloatingPositionXml,
        OverlapXml = over.OverlapXml ?? current.OverlapXml,
        RightToLeft = over.RightToLeft ?? current.RightToLeft,
        RowBandSize = over.RowBandSize ?? current.RowBandSize,
        ColumnBandSize = over.ColumnBandSize ?? current.ColumnBandSize,
        Width = over.Width ?? current.Width,
        Alignment = over.Alignment ?? current.Alignment,
        CellSpacing = over.CellSpacing ?? current.CellSpacing,
        Indent = over.Indent ?? current.Indent,
        Borders = over.Borders ?? current.Borders,
        Shading = over.Shading ?? current.Shading,
        Layout = over.Layout ?? current.Layout,
        CellMargins = over.CellMargins ?? current.CellMargins,
        StyleOptions = over.StyleOptions ?? current.StyleOptions,
        Caption = over.Caption ?? current.Caption,
        Description = over.Description ?? current.Description,
        ChangeXml = over.ChangeXml ?? current.ChangeXml,
        Extensions = over.Extensions ?? current.Extensions,
    };

    private static TableRowFormat Apply(TableRowFormat current, TableRowFormat over) => current with
    {
        ConditionalFormattingXml = over.ConditionalFormattingXml ?? current.ConditionalFormattingXml,
        DivIdXml = over.DivIdXml ?? current.DivIdXml,
        GridBefore = over.GridBefore ?? current.GridBefore,
        GridAfter = over.GridAfter ?? current.GridAfter,
        WidthBefore = over.WidthBefore ?? current.WidthBefore,
        WidthAfter = over.WidthAfter ?? current.WidthAfter,
        CannotSplit = over.CannotSplit ?? current.CannotSplit,
        Height = over.Height ?? current.Height,
        HeightRule = over.HeightRule ?? current.HeightRule,
        IsHeader = over.IsHeader ?? current.IsHeader,
        CellSpacing = over.CellSpacing ?? current.CellSpacing,
        Alignment = over.Alignment ?? current.Alignment,
        Hidden = over.Hidden ?? current.Hidden,
        InsertedXml = over.InsertedXml ?? current.InsertedXml,
        DeletedXml = over.DeletedXml ?? current.DeletedXml,
        ChangeXml = over.ChangeXml ?? current.ChangeXml,
        Extensions = over.Extensions ?? current.Extensions,
    };

    private static TableCellFormat Apply(TableCellFormat current, TableCellFormat over) => current with
    {
        ConditionalFormattingXml = over.ConditionalFormattingXml ?? current.ConditionalFormattingXml,
        Width = over.Width ?? current.Width,
        GridSpan = over.GridSpan ?? current.GridSpan,
        HorizontalMergeXml = over.HorizontalMergeXml ?? current.HorizontalMergeXml,
        VerticalMerge = over.VerticalMerge ?? current.VerticalMerge,
        Borders = over.Borders ?? current.Borders,
        Shading = over.Shading ?? current.Shading,
        NoWrap = over.NoWrap ?? current.NoWrap,
        Margins = over.Margins ?? current.Margins,
        TextDirection = over.TextDirection ?? current.TextDirection,
        FitText = over.FitText ?? current.FitText,
        VerticalAlignment = over.VerticalAlignment ?? current.VerticalAlignment,
        HideMark = over.HideMark ?? current.HideMark,
        HeadersXml = over.HeadersXml ?? current.HeadersXml,
        RevisionXml = over.RevisionXml ?? current.RevisionXml,
        Extensions = over.Extensions ?? current.Extensions,
    };
}
