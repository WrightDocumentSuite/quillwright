using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Pdf.Layout;

/// <summary>
/// Headers and footers: which one a page gets, how much room it takes and where it is drawn.
/// </summary>
/// <remarks>
/// They are laid out after the body rather than with it, because a header usually prints the page
/// number and the page number is the one thing the body cannot know until it has finished
/// falling into pages. What the body does need in advance is how tall they are, which is measured
/// once per section before its first page opens.
/// </remarks>
internal sealed partial class PageComposer
{
    private readonly Dictionary<Section, (double Header, double Footer)> _furniture = [];

    /// <summary>How much room the tallest header and footer of a section take.</summary>
    private (double Header, double Footer) FurnitureHeights(Section section)
    {
        if (_furniture.TryGetValue(section, out (double Header, double Footer) cached))
            return cached;

        double width = PageGeometry.From(section.Properties).ContentWidth;
        double header = 0;
        double footer = 0;

        foreach (HeaderFooterKind kind in (ReadOnlySpan<HeaderFooterKind>)
            [HeaderFooterKind.Default, HeaderFooterKind.First, HeaderFooterKind.Even])
        {
            if (Slot(section, kind, footer: false) is { } top)
                header = Math.Max(header, HeightOf(top, width));

            if (Slot(section, kind, footer: true) is { } bottom)
                footer = Math.Max(footer, HeightOf(bottom, width));
        }

        _furniture[section] = (header, footer);
        return (header, footer);
    }

    /// <summary>
    /// The header or footer a section uses for a slot, following "link to previous" back through
    /// the sections before it when this one defines nothing.
    /// </summary>
    private HeaderFooter? Slot(Section section, HeaderFooterKind kind, bool footer)
    {
        IReadOnlyList<Section> sections = _context.Source.Sections;
        int index = -1;
        for (int i = 0; i < sections.Count; i++)
        {
            if (ReferenceEquals(sections[i], section))
            {
                index = i;
                break;
            }
        }

        for (int i = index; i >= 0; i--)
        {
            HeaderFooterSlots slots = footer ? sections[i].Footers : sections[i].Headers;
            if (slots[kind] is { } defined)
                return defined;

            // A section that defines the default slot has spoken; it does not inherit the rest.
            if (slots.Default is not null)
                return null;
        }

        return null;
    }

    /// <summary>Which slot a page uses, given what the section and the document turned on.</summary>
    private HeaderFooter? For(ComposedPage page, bool footer)
    {
        Section section = page.Section;

        if (section.Properties.DifferentFirstPage && page.IsSectionStart)
            return Slot(section, HeaderFooterKind.First, footer);

        if (_context.Source.Settings.EvenAndOddHeaders && page.Number % 2 == 0)
            return Slot(section, HeaderFooterKind.Even, footer);

        return Slot(section, HeaderFooterKind.Default, footer);
    }

    private double HeightOf(HeaderFooter container, double width)
    {
        double total = 0;
        foreach (BlockBox block in MeasureContainer(container, width))
            total += block.TotalHeight;

        return total;
    }

    /// <summary>Lays out the blocks of a header or a footer, without disturbing the body's counters.</summary>
    private List<BlockBox> MeasureContainer(HeaderFooter container, double width)
    {
        bool wasRehearsing = _rehearsing;
        _rehearsing = true;

        try
        {
            return MeasureBlocks(container.Blocks, width);
        }
        finally
        {
            _rehearsing = wasRehearsing;
        }
    }

    /// <summary>Lays out a run of blocks against a width, without placing any of them.</summary>
    private List<BlockBox> MeasureBlocks(IEnumerable<Block> blocks, double width)
    {
        List<BlockBox> boxes = [];

        foreach (Block block in blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    boxes.Add(_layouter.Layout(paragraph, width));
                    break;

                case Table table:
                    boxes.Add(_tables.Measure(table, width));
                    break;

                default:
                    break;
            }
        }

        return boxes;
    }

    /// <summary>Draws the header and the footer of every page, now that the numbers are known.</summary>
    private void FinishPages()
    {
        foreach (ComposedPage page in _pages)
        {
            _page = page;
            PageGeometry geometry = page.Geometry;

            if (For(page, footer: false) is { } header)
                Draw(page, header, geometry.HeaderDistance, geometry.ContentLeft, geometry.ContentWidth);

            if (For(page, footer: true) is { } footer)
            {
                List<BlockBox> blocks = MeasureContainer(footer, geometry.ContentWidth);
                double height = blocks.Sum(static block => block.TotalHeight);
                double top = geometry.Height - geometry.FooterDistance - height;
                Draw(page, blocks, top, geometry.ContentLeft);
            }
        }
    }

    private void Draw(ComposedPage page, HeaderFooter container, double top, double left, double width) =>
        Draw(page, MeasureContainer(container, width), top, left);

    private void Draw(ComposedPage page, List<BlockBox> blocks, double top, double left) =>
        Stack(page, blocks, top, left, furniture: true);

    /// <summary>
    /// Draws blocks in the body rather than in the furniture, one after another from a given
    /// point. Nothing here breaks across pages: the caller has already found the room.
    /// </summary>
    private double DrawBlocks(List<BlockBox> blocks, double left, double top) =>
        Stack(Current, blocks, top, left, furniture: false);

    private double Stack(ComposedPage page, List<BlockBox> blocks, double top, double left, bool furniture)
    {
        List<PageItem> target = furniture ? page.Furniture : page.Items;
        double y = top;

        foreach (BlockBox block in blocks)
        {
            y += block.SpacingBefore;

            if (block is TableBox table)
            {
                y = DrawStandaloneTable(page, table, left, y, furniture);
                continue;
            }

            if (block is not ParagraphBox paragraph)
                continue;

            foreach (LineBox line in paragraph.Lines)
            {
                target.Add(new TextLineItem
                {
                    Line = line,
                    X = left + line.IndentLeft,
                    Y = y,
                    Tag = furniture ? FurnitureTag(paragraph) : TagOf(paragraph),
                });

                DrawInlineShapes(line, left + line.IndentLeft, y, furniture);
                y += line.Height;
            }

            y += block.SpacingAfter;
        }

        return y;
    }

    /// <summary>Draws a table where it stands rather than flowing it, whole.</summary>
    private double DrawStandaloneTable(ComposedPage page, TableBox table, double left, double top, bool furniture)
    {
        // The table placement machinery draws through the cursor, so it is borrowed by pointing
        // the cursor at where this table goes and putting the cursor back afterwards.
        int before = page.Items.Count;
        double saved = _cursor;
        ComposedPage? savedPage = _page;

        _page = page;
        _cursor = top;

        for (int index = 0; index < table.Rows.Count; index++)
        {
            DrawRow(table, index, left + table.Offset, table.Rows[index].Height);
            _cursor += table.Rows[index].Height;
        }

        double bottom = _cursor;
        _cursor = saved;
        _page = savedPage;

        if (!furniture)
            return bottom;

        for (int i = before; i < page.Items.Count; i++)
            page.Furniture.Add(page.Items[i]);

        page.Items.RemoveRange(before, page.Items.Count - before);
        return bottom;
    }
}
