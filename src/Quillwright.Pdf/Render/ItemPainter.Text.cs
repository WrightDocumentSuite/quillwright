using Inkwright;
using Inkwright.Cos;
using Quillwright.Pdf.Layout;
using Quillwright.Styles;

namespace Quillwright.Pdf.Render;

/// <summary>
/// Drawing text: the glyphs themselves, what is painted behind them and what is ruled through
/// and under them.
/// </summary>
internal sealed partial class ItemPainter
{
    private void PaintFieldText(PageFieldFragment field, double x, double baseline, TagRef? tag)
    {
        string text = _resolveField(field);
        if (text.Length == 0)
            return;

        var fragment = new TextFragment { Text = text, Style = field.Style, Width = field.Width };
        PaintText(fragment, x, baseline, extraSpace: 0, tag);
    }

    /// <summary>
    /// Draws an equation: every glyph run at the place the layout put it, then every line —
    /// fraction bars, radical roofs, the edges of a framed box — in the colour of the text.
    /// </summary>
    private void PaintEquation(EquationFragment fragment, double x, double baseline, TagRef? tag)
    {
        int mcid = BeginTag(tag);

        foreach (EquationMark mark in fragment.Layout.Marks)
        {
            var run = new TextFragment { Text = mark.Text, Style = mark.Style, Width = mark.Style.Measure(mark.Text) };
            PaintText(run, x + mark.X, baseline + mark.Y, extraSpace: 0, tag: null, marked: false);
        }

        EndTag(tag, mcid);

        foreach (EquationRule rule in fragment.Layout.Rules)
        {
            PaintStroke(new StrokeItem
            {
                X = x + rule.X,
                Y = baseline + rule.Y,
                X2 = x + rule.X2,
                Y2 = baseline + rule.Y2,
                Thickness = Math.Max(0.4, rule.Thickness),
                Color = fragment.Layout.Marks.Count > 0 ? fragment.Layout.Marks[0].Style.Color : PdfColor.Black,
            });
        }
    }

    private void PaintText(
        TextFragment fragment, double x, double baseline, double extraSpace, TagRef? tag, bool marked = true)
    {
        CharacterStyle style = fragment.Style;
        string text = style.Caps || style.SmallCaps ? fragment.Shown.ToUpperInvariant() : fragment.Shown;
        if (text.Length == 0)
            return;

        double width = fragment.Width + (extraSpace * fragment.SpaceCount);
        PaintTextBackground(fragment, x, baseline, width);

        // An equation opens one marked-content sequence round the whole of itself, so the runs
        // inside it must not each open another.
        int mcid = marked ? BeginTag(tag) : -1;
        _canvas.Save().BeginText().FillColor(style.Color).Font(style.Font, style.FontSize);

        if (style.HorizontalScale is not 1)
            _canvas.HorizontalScale(style.HorizontalScale * 100);

        if (style.CharacterSpacing is not 0)
            _canvas.CharSpacing(style.CharacterSpacing);

        double y = _geometry.ToPdfY(baseline) + style.Rise;
        _canvas.TextMatrix(PdfMatrix.Translation(x, y));

        if (style.SmallCaps)
            ShowSmallCaps(fragment.Shown, style);
        else
            Show(text, style, extraSpace);

        _canvas.EndText().Restore();
        if (marked)
            EndTag(tag, mcid);

        PaintTextDecoration(fragment, x, baseline, width);
    }

    private void PaintTextBackground(TextFragment fragment, double x, double baseline, double width)
    {
        CharacterStyle style = fragment.Style;
        PdfColor? background = style.Highlight ?? style.Shading;
        if (background is not { } color || width <= 0)
            return;

        double top = baseline - style.Ascent;
        double height = style.Ascent + style.Descent;
        PaintFill(new FillItem { X = x, Y = top, Width = width, Height = height, Color = color });
    }

    private void PaintTextDecoration(TextFragment fragment, double x, double baseline, double width)
    {
        CharacterStyle style = fragment.Style;
        if (width <= 0)
            return;

        // Word draws no underline under the spaces between words when the style says "words only".
        bool skipSpaces = style.Underline == UnderlineStyle.Words;

        if (style.Underline is not UnderlineStyle.None)
        {
            double thickness = Math.Max(0.4, style.FontSize * UnderlineWeight);
            double y = baseline + (style.FontSize * UnderlineOffset) - style.Rise;
            BorderStyle dash = UnderlineDash(style.Underline);

            if (skipSpaces && fragment.SpaceCount > 0)
                UnderlineWords(fragment, x, y, thickness, style, dash);
            else
                Rule(x, y, x + width, thickness, style.UnderlineColor, dash);

            if (style.Underline is UnderlineStyle.Double or UnderlineStyle.WavyDouble)
                Rule(x, y + (thickness * 2), x + width, thickness, style.UnderlineColor, dash);
        }

        if (style.Strike || style.DoubleStrike)
        {
            double thickness = Math.Max(0.4, style.FontSize * UnderlineWeight);
            double y = baseline - (style.FontSize * StrikeOffset) - style.Rise;

            if (style.DoubleStrike)
            {
                Rule(x, y - thickness, x + width, thickness, style.Color, BorderStyle.Single);
                Rule(x, y + thickness, x + width, thickness, style.Color, BorderStyle.Single);
            }
            else
            {
                Rule(x, y, x + width, thickness, style.Color, BorderStyle.Single);
            }
        }
    }

    /// <summary>Underlines the words of a fragment but not the spaces between them.</summary>
    private void UnderlineWords(
        TextFragment fragment, double x, double y, double thickness, CharacterStyle style, BorderStyle dash)
    {
        double cursor = x;
        foreach (Range range in SplitKeepingSeparators(fragment.Text))
        {
            ReadOnlySpan<char> part = fragment.Text.AsSpan()[range];
            double partWidth = style.Measure(part);
            if (part.Length > 0 && part[0] != ' ')
                Rule(cursor, y, cursor + partWidth, thickness, style.UnderlineColor, dash);

            cursor += partWidth;
        }
    }

    private static IEnumerable<Range> SplitKeepingSeparators(string text)
    {
        int start = 0;
        for (int i = 1; i <= text.Length; i++)
        {
            bool boundary = i == text.Length || (text[i] == ' ') != (text[i - 1] == ' ');
            if (!boundary)
                continue;

            yield return new Range(start, i);
            start = i;
        }
    }

    private static BorderStyle UnderlineDash(UnderlineStyle underline) => underline switch
    {
        UnderlineStyle.Dotted or UnderlineStyle.DottedHeavy => BorderStyle.Dotted,
        UnderlineStyle.Dash or UnderlineStyle.DashedHeavy or UnderlineStyle.DashLong or UnderlineStyle.DashLongHeavy
            => BorderStyle.Dashed,
        UnderlineStyle.DotDash or UnderlineStyle.DashDotHeavy => BorderStyle.DotDash,
        UnderlineStyle.DotDotDash or UnderlineStyle.DashDotDotHeavy => BorderStyle.DotDotDash,
        _ => BorderStyle.Single,
    };

    private void Rule(double x1, double y, double x2, double thickness, PdfColor color, BorderStyle style) =>
        PaintStroke(new StrokeItem
        {
            X = x1,
            Y = y,
            X2 = x2,
            Y2 = y,
            Thickness = thickness,
            Color = color,
            Style = style,
        });

    /// <summary>
    /// Shows a string, spreading justification across its spaces. The adjustment goes into a
    /// positioned-text array rather than into the word-spacing operator, because word spacing only
    /// affects the single byte 32 and an embedded font is addressed two bytes at a time.
    /// </summary>
    private void Show(string text, CharacterStyle style, double extraSpace)
    {
        if (extraSpace <= 0 || !text.Contains(' ', StringComparison.Ordinal))
        {
            _canvas.ShowText(text);
            return;
        }

        double adjustment = -extraSpace * 1000 / style.FontSize;
        List<PdfValue> parts = [];
        int start = 0;

        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != ' ')
                continue;

            parts.Add(PdfValue.String(style.Font.Encode(text[start..(i + 1)])));
            parts.Add(PdfValue.Number(adjustment));
            start = i + 1;
        }

        if (start < text.Length)
            parts.Add(PdfValue.String(style.Font.Encode(text[start..])));

        _canvas.ShowTextAdjusted(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(parts));
    }

    /// <summary>
    /// Shows text as small capitals: every letter is a capital, and the ones that were lower case
    /// are drawn smaller. Switching size ends the run, so the text is shown in alternating groups.
    /// </summary>
    private void ShowSmallCaps(string text, CharacterStyle style)
    {
        int start = 0;
        bool small = text.Length > 0 && char.IsLower(text[0]);

        for (int i = 1; i <= text.Length; i++)
        {
            bool next = i < text.Length && char.IsLower(text[i]);
            if (i < text.Length && next == small)
                continue;

            string part = text[start..i].ToUpperInvariant();
            _canvas.Font(style.Font, small ? style.SmallCapsSize : style.FontSize).ShowText(part);
            start = i;
            small = next;
        }
    }
}
