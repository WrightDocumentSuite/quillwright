using Inkwright;
using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Pdf.Layout;

/// <summary>Page and section bookkeeping, and the decoration that surrounds a block.</summary>
internal sealed partial class PageComposer
{
    private Section _section = null!;

    private void ResetNumbering()
    {
        _pages.Clear();
        _bookmarks.Clear();
        _shapeContent.Clear();
        _page = null;
        _nextNumber = 1;
        _hasContent = false;
    }

    /// <summary>
    /// Opens the first page of a section, honouring where the section says it begins. A continuous
    /// section carries on down the page it starts on; every other kind opens a page, and an even-
    /// or odd-page section may have to leave a blank one behind to land on the right side.
    /// </summary>
    private void StartSection(Section section)
    {
        _section = section;
        SectionStart start = section.Properties.Start;

        if (section.Properties.TextDirection is { } direction && direction != TextDirection.LeftToRightTopToBottom)
        {
            _context.Diagnostics.Add(
                PdfExportWarningKind.LayoutApproximated,
                "A section whose text flows vertically is laid out the ordinary way.",
                "vertical-section");
        }

        if (start == SectionStart.Continuous && _page is not null)
        {
            // Plain text flowing into plain text carries on down the page. Anything else — a
            // band of columns ending, or beginning — closes the old band and opens a new one
            // below it, which is what a continuous break does to a page.
            ColumnBand next = ColumnBand.Of(section.Properties, Current.Geometry);
            if (_columns.Count == 1 && next.Matches(_columns))
                return;

            StartBandBelow(next);
            return;
        }

        ClosePage();
        StartPage(section, sectionStart: true);

        if (start is SectionStart.EvenPage or SectionStart.OddPage)
        {
            bool wantsEven = start == SectionStart.EvenPage;
            if (_page!.Number % 2 == 0 != wantsEven)
            {
                ClosePage();
                StartPage(section, sectionStart: true);
            }
        }
    }

    /// <summary>Finishes the page that is closing: the rules between its columns, then its notes.</summary>
    private void ClosePage()
    {
        CloseColumns();
        FlushNotes();
    }

    /// <summary>Opens a page under the current section's page setup.</summary>
    private ComposedPage StartPage(Section section, bool sectionStart)
    {
        if (_pages.Count >= _context.Options.MaxPages)
        {
            throw new InvalidOperationException(
                $"The document did not finish within {_context.Options.MaxPages} pages. Content that " +
                "cannot fit on a page would otherwise paginate for ever; raise " +
                $"{nameof(PdfExportOptions)}.{nameof(PdfExportOptions.MaxPages)} if the document really is this long.");
        }

        if (sectionStart && section.Properties.PageNumbering.Start is { } restart)
            _nextNumber = restart;

        bool mirrored = _nextNumber % 2 == 0 && section.Properties.RightToLeftGutter;
        (double header, double footer) = FurnitureHeights(section);
        var page = new ComposedPage(
            PageGeometry.From(section.Properties, mirrored, header, footer),
            section,
            _pages.Count,
            _nextNumber)
        {
            IsSectionStart = sectionStart,
        };

        _pages.Add(page);
        _page = page;
        _section = section;
        _nextNumber++;
        _cursor = page.Geometry.ContentTop;
        _hasContent = false;
        _wrapObstacles.Clear();
        ResetColumns(ColumnBand.Of(section.Properties, page.Geometry), page.Geometry.ContentTop);
        MarkBandStart();
        return page;
    }

    /// <summary>Opens the next page of the section already being laid out.</summary>
    private void NewPage()
    {
        // The rules and the notes belong to the page that is closing, and only now is it certain
        // what they are.
        ClosePage();
        StartPage(_section, sectionStart: false);
    }


    /// <summary>Draws the background and the border a block asks for, around the part on this page.</summary>
    /// <param name="box">The paragraph being placed.</param>
    /// <param name="index">The first line placed here.</param>
    /// <param name="count">How many lines are placed here.</param>
    /// <param name="left">The left edge of the container.</param>
    /// <param name="top">Where the first line starts, measured down from the top of the page.</param>
    /// <param name="height">How tall the placed lines are together.</param>
    private void Decorate(ParagraphBox box, int index, int count, double left, double top, double height)
    {
        if (height <= 0)
            return;

        double x1 = left + box.IndentLeft;
        double x2 = left + box.ContainerWidth - box.IndentRight;
        if (x2 <= x1)
            return;

        if (box.Shading is { } shading && Fill(shading) is { } color)
        {
            Current.Items.Add(new FillItem
            {
                X = x1,
                Y = top,
                Width = x2 - x1,
                Height = height,
                Color = color,
            });
        }

        if (box.Borders is not { } borders || borders.IsEmpty)
            return;

        bool first = index == 0;
        bool last = index + count == box.Lines.Count;

        if (first)
            Edge(borders.Top, x1, top, x2, top, above: true);

        if (last)
            Edge(borders.Bottom, x1, top + height, x2, top + height, above: false);

        Edge(borders.Left, x1, top, x1, top + height, above: false, leading: true);
        Edge(borders.Right, x2, top, x2, top + height, above: false, leading: false);
        Edge(borders.Bar, x1, top, x1, top + height, above: false, leading: true);

        void Edge(BorderLine? line, double ax, double ay, double bx, double by, bool above, bool? leading = null)
        {
            if (line is null || line.IsEmpty)
                return;

            double space = line.Space.Points;
            if (leading is null)
            {
                ay += above ? -space : space;
                by = ay;
            }
            else
            {
                double offset = leading.Value ? -space : space;
                ax += offset;
                bx += offset;
            }

            Current.Items.Add(new StrokeItem
            {
                X = ax,
                Y = ay,
                X2 = bx,
                Y2 = by,
                Thickness = Math.Max(0.25, line.Width.Points),
                Color = _context.ColorOf(line.Color, PdfColor.Black),
                Style = line.Style,
            });
        }
    }

    private PdfColor? Fill(Shading shading)
    {
        if (shading.IsEmpty || shading.Pattern is ShadingPattern.Nil)
            return null;

        Primitives.WordColor value = shading.Pattern == ShadingPattern.Solid ? shading.Color : shading.Fill;
        return value.IsAuto ? null : _context.ColorOf(value, PdfColor.White);
    }
}
