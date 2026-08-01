using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Pdf.Layout;

/// <summary>
/// The pieces of a line that are not text: the jump a tab makes, the room a picture takes and the
/// space left for a number nobody knows yet.
/// </summary>
internal sealed partial class LineBreaker
{
    private void AddTab(InlineItem item)
    {
        ResolveTab();

        double absolute = _line.IndentLeft + _x;
        TabStop stop = _metrics.NextStop(absolute);
        double target = stop.Position.Points - _line.IndentLeft;

        if (stop.Alignment == TabAlignment.Bar)
        {
            _line.Fragments.Add(new TabFragment
            {
                Style = item.Style,
                X = target,
                Width = 0,
                Ascent = item.Style.Ascent,
                Descent = item.Style.Descent,
                LineHeight = item.Style.LineHeight,
                IsBar = true,
                Link = item.Link,
            });
            return;
        }

        if (target > _line.AvailableWidth)
        {
            // No stop is left on this line, so the tab acts as the break Word makes it.
            BreakLine();
            return;
        }

        _line.Fragments.Add(new TabFragment
        {
            Style = item.Style,
            Leader = stop.Leader,
            Alignment = stop.Alignment,
            X = _x,
            Width = Math.Max(0, target - _x),
            Ascent = item.Style.Ascent,
            Descent = item.Style.Descent,
            LineHeight = item.Style.LineHeight,
            Link = item.Link,
        });

        if (stop.Alignment is not TabAlignment.Left)
            _pending = new PendingTab(_line.Fragments.Count - 1, _x, target, stop.Alignment);

        _x = target;
    }

    private void AddPicture(InlineItem item)
    {
        Picture picture = item.Picture!;
        double width = picture.Width.Points;
        double height = picture.Height.Points;

        if (width <= 0 || height <= 0)
            return;

        if (_x + width > _line.AvailableWidth && _line.Fragments.Count > 0)
            BreakLine();

        _line.Fragments.Add(new ImageFragment
        {
            Picture = picture,
            X = _x,
            Width = width,

            // An inline picture stands on the baseline, so all of it is above.
            Ascent = height,
            Descent = 0,
            LineHeight = height,
            Link = item.Link,
        });

        _x += width;
    }

    /// <summary>Reserves the room an inline text box takes; the box is drawn where the line lands.</summary>
    private void AddShape(InlineItem item)
    {
        Shape shape = item.Shape!;
        double width = shape.Width.Points;
        double height = shape.Height.Points;

        if (width <= 0 || height <= 0)
            return;

        if (_x + width > _line.AvailableWidth && _line.Fragments.Count > 0)
            BreakLine();

        _line.Fragments.Add(new ShapeFragment
        {
            Shape = shape,
            X = _x,
            Width = width,

            // An inline text box stands on the baseline the way a picture does.
            Ascent = height,
            Descent = 0,
            LineHeight = height,
            Link = item.Link,
        });

        _x += width;
    }

    /// <summary>
    /// Reserves the room an equation takes. An equation is one piece: it is broken across lines
    /// by nobody, here or in Word, so it goes on a line of its own when it will not fit.
    /// </summary>
    private void AddEquation(InlineItem item)
    {
        EquationLayout equation = item.Equation!;
        if (equation.Width <= 0 && equation.Height <= 0)
            return;

        if (_x + equation.Width > _line.AvailableWidth && _line.Fragments.Count > 0)
            BreakLine();

        _line.Fragments.Add(new EquationFragment
        {
            Layout = equation,
            X = _x,
            Width = equation.Width,
            Ascent = equation.Ascent,
            Descent = equation.Descent,

            // An equation raises the line it is on the way a tall glyph would, rather than the
            // way a picture does: it is text, and the leading round it should follow the text.
            LineHeight = Math.Max(item.Style.LineHeight, equation.Height),
            Link = item.Link,
        });

        _x += equation.Width;
    }

    /// <summary>Reserves the frame a chart was given, which is a fixed box like a picture's.</summary>
    private void AddChart(InlineItem item)
    {
        ChartLayout chart = item.Chart!;
        if (chart.Width <= 0 || chart.Height <= 0)
            return;

        if (_x + chart.Width > _line.AvailableWidth && _line.Fragments.Count > 0)
            BreakLine();

        _line.Fragments.Add(new ChartFragment
        {
            Layout = chart,
            X = _x,
            Width = chart.Width,
            Ascent = chart.Height,
            Descent = 0,
            LineHeight = chart.Height,
            Link = item.Link,
        });

        _x += chart.Width;
    }

    /// <summary>
    /// Places the mark that stands for a note, and puts the note on whichever line the mark ended
    /// up on. Which line that is matters: the note has to be printed on the page that line lands
    /// on, and a mark at the very end of a line may have pushed itself onto the next one.
    /// </summary>
    private void AddNoteReference(InlineItem item)
    {
        if (item.Note is not { } note)
            return;

        if (note.Number.Length > 0)
            AddText(item with { Kind = InlineKind.Text, Text = note.Number });

        _line.Notes.Add(note);
    }

    private void AddPageField(InlineItem item)
    {
        string estimate = EstimateField(item.Field, item.FieldFormat, item.FieldBookmark);
        double width = item.Style.Measure(estimate);

        if (_x + width > _line.AvailableWidth && _line.Fragments.Count > 0)
            BreakLine();

        _line.Fragments.Add(new PageFieldFragment
        {
            Kind = item.Field,
            Format = item.FieldFormat,
            FormatStated = item.FieldFormatStated,
            Bookmark = item.FieldBookmark,
            Style = item.Style,
            Estimate = estimate,
            X = _x,
            Width = width,
            Ascent = item.Style.Ascent,
            Descent = item.Style.Descent,
            LineHeight = item.Style.LineHeight,
            Link = item.Link,
        });

        _x += width;
    }
}
