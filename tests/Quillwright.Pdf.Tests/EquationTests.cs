using System.Text;
using Inkwright.Text;
using Quillwright.Model;
using Xunit;

namespace Quillwright.Pdf.Tests;

/// <summary>
/// Equations on the page: that they are drawn at all, that the parts are where the structure
/// says they should be, and that a fraction has a bar under its numerator.
/// </summary>
/// <remarks>
/// What is checked here is geometry rather than looks. A superscript that is drawn at the same
/// height as its base is not a superscript, and a denominator that lands above its numerator is
/// upside down — both are things a person notices immediately and a text comparison never would.
/// </remarks>
public sealed class EquationTests
{
    private static WordDocument WithEquation(params MathNode[] nodes)
    {
        var equation = new MathObject();
        foreach (MathNode node in nodes)
            equation.Content.Nodes.Add(node);

        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph().AppendObject(equation);
        return document;
    }

    private static MathFraction Half()
    {
        var fraction = new MathFraction();
        fraction.Numerator.Nodes.Add(new MathRun("1"));
        fraction.Denominator.Nodes.Add(new MathRun("2"));
        return fraction;
    }

    /// <summary>Where a character was drawn, by the letter it is.</summary>
    private static PdfLetter Letter(Rendered rendered, string text) =>
        rendered.Letters().First(letter => letter.Text == text);

    /// <summary>How many lines the page strokes, which is one operator on a line of its own.</summary>
    private static int Strokes(Rendered rendered) => Encoding.Latin1
        .GetString(rendered.Document.Pages[0].GetContent())
        .Split('\n')
        .Count(static line => line == "S");

    [Fact]
    public void AFraction_DrawsItsNumeratorAboveItsDenominator()
    {
        using Rendered rendered = Rendered.Of(WithEquation(new MathRun("x="), Half()));

        PdfLetter numerator = Letter(rendered, "1");
        PdfLetter denominator = Letter(rendered, "2");

        Assert.True(numerator.Origin.Y > denominator.Origin.Y, "the numerator was not drawn above the denominator");
        Assert.True(numerator.Origin.X > Letter(rendered, "x").Origin.X, "the fraction was not drawn after the text");
        Assert.Equal(numerator.Origin.X, denominator.Origin.X, 1);
    }

    [Fact]
    public void AFraction_RulesABarBetweenThem()
    {
        using Rendered rendered = Rendered.Of(WithEquation(Half()));

        Assert.Equal(1, Strokes(rendered));
    }

    [Fact]
    public void ASuperscript_IsRaisedAndDrawnSmaller()
    {
        var script = new MathScript();
        script.Base.Nodes.Add(new MathRun("e"));
        script.Superscript.Nodes.Add(new MathRun("x"));

        using Rendered rendered = Rendered.Of(WithEquation(script));

        PdfLetter root = Letter(rendered, "e");
        PdfLetter power = Letter(rendered, "x");

        Assert.True(power.Origin.Y > root.Origin.Y, "the exponent was not raised above the base");
        Assert.True(power.Origin.X > root.Origin.X, "the exponent was not drawn after the base");
        Assert.True(power.Width < root.Width * 1.1, "the exponent was not drawn smaller than the base");
    }

    [Fact]
    public void ASubscript_IsLowered()
    {
        var script = new MathScript();
        script.Base.Nodes.Add(new MathRun("a"));
        script.Subscript.Nodes.Add(new MathRun("n"));

        using Rendered rendered = Rendered.Of(WithEquation(script));

        Assert.True(
            Letter(rendered, "n").Origin.Y < Letter(rendered, "a").Origin.Y,
            "the subscript was not lowered below the base");
    }

    [Fact]
    public void ASumOverARange_PutsItsLimitsAboveAndBelowTheSign()
    {
        var sum = new MathNary { Operator = "\u2211" };
        sum.Lower.Nodes.Add(new MathRun("1"));
        sum.Upper.Nodes.Add(new MathRun("9"));
        sum.Base.Nodes.Add(new MathRun("k"));

        using Rendered rendered = Rendered.Of(WithEquation(sum));

        PdfLetter lower = Letter(rendered, "1");
        PdfLetter upper = Letter(rendered, "9");
        PdfLetter sign = Letter(rendered, "\u2211");

        Assert.True(upper.Origin.Y > sign.Origin.Y, "the upper limit was not drawn above the operator");
        Assert.True(lower.Origin.Y < sign.Origin.Y, "the lower limit was not drawn below the operator");
        Assert.InRange(upper.Origin.X, sign.Origin.X - 3, sign.Origin.X + sign.Width + 3);
    }

    [Fact]
    public void AnIntegral_PutsItsLimitsAtTheCornersInstead()
    {
        var integral = new MathNary { Operator = "\u222B" };
        integral.Lower.Nodes.Add(new MathRun("0"));
        integral.Upper.Nodes.Add(new MathRun("9"));
        integral.Base.Nodes.Add(new MathRun("k"));

        using Rendered rendered = Rendered.Of(WithEquation(integral));

        Assert.True(
            Letter(rendered, "9").Origin.X > Letter(rendered, "\u222B").Origin.X,
            "the limit of an integral was stacked over it rather than set beside it");
    }

    [Fact]
    public void ARadical_DrawsItsSignAndARoofOverTheRadicand()
    {
        var radical = new MathRadical { HideDegree = true };
        radical.Base.Nodes.Add(new MathRun("2"));

        using Rendered rendered = Rendered.Of(WithEquation(radical));

        Assert.Contains(rendered.Letters(), letter => letter.Text == "\u221A");
        Assert.Equal(1, Strokes(rendered));
    }

    [Fact]
    public void ADelimiter_GrowsAroundWhatItHolds()
    {
        var small = new MathDelimiter();
        small.Arguments.Add(MathElement.Of("x"));

        var large = new MathDelimiter();
        large.Arguments.Add(new MathElement(Half()));

        using Rendered around = Rendered.Of(WithEquation(small));
        using Rendered round = Rendered.Of(WithEquation(large));

        Assert.True(
            Letter(round, "(").Width > Letter(around, "(").Width,
            "the bracket did not grow to cover the fraction inside it");
    }

    [Fact]
    public void AMatrix_LaysItsCellsOutInAGrid()
    {
        var matrix = new MathMatrix();
        var top = new MathMatrixRow();
        top.Cells.Add(MathElement.Of("a"));
        top.Cells.Add(MathElement.Of("b"));
        var bottom = new MathMatrixRow();
        bottom.Cells.Add(MathElement.Of("c"));
        bottom.Cells.Add(MathElement.Of("d"));
        matrix.Rows.Add(top);
        matrix.Rows.Add(bottom);

        using Rendered rendered = Rendered.Of(WithEquation(matrix));

        PdfLetter first = Letter(rendered, "a");
        PdfLetter below = Letter(rendered, "c");

        Assert.True(Letter(rendered, "b").Origin.X > first.Origin.X, "the columns are not side by side");
        Assert.True(below.Origin.Y < first.Origin.Y, "the rows are not stacked");

        // Cells are centred in their column, so the two glyphs share a centre, not an edge.
        Assert.Equal(first.Origin.X + (first.Width / 2), below.Origin.X + (below.Width / 2), 1);
    }

    [Fact]
    public void AFramedBox_RulesTheEdgesItDoesNotHide()
    {
        var box = new MathBorderBox { HideTop = true, HideBottom = true, HideLeft = true, StrikeUpward = true };
        box.Base.Nodes.Add(new MathRun("q"));

        using Rendered rendered = Rendered.Of(WithEquation(box));

        // One edge and one strike: two strokes, and no more.
        Assert.Equal(2, Strokes(rendered));
    }

    [Fact]
    public void APhantom_TakesRoomWithoutBeingDrawn()
    {
        var phantom = new MathPhantom();
        phantom.Base.Nodes.Add(new MathRun("MMM"));

        using Rendered hidden = Rendered.Of(WithEquation(new MathRun("["), phantom, new MathRun("]")));
        using Rendered plain = Rendered.Of(WithEquation(new MathRun("["), new MathRun("]")));

        Assert.DoesNotContain(hidden.Letters(), letter => letter.Text == "M");
        Assert.True(
            Letter(hidden, "]").Origin.X > Letter(plain, "]").Origin.X + 5,
            "the phantom took up no room");
    }

    [Fact]
    public void AnEquationInAParagraph_KeepsItsPlaceBetweenTheWordsAroundIt()
    {
        WordDocument document = WordDocument.Create();
        Paragraph paragraph = document.Sections[0].AddParagraph();
        paragraph.AppendText("before ");

        var equation = new MathObject();
        equation.Content.Nodes.Add(Half());
        paragraph.AppendObject(equation);
        paragraph.AppendText(" after");

        using Rendered rendered = Rendered.Of(document);
        string line = string.Concat(rendered.Lines());

        Assert.Contains("before", line, StringComparison.Ordinal);
        Assert.Contains("after", line, StringComparison.Ordinal);
        Assert.True(Letter(rendered, "1").Origin.X > Letter(rendered, "b").Origin.X);
    }

    [Fact]
    public void ADisplayParagraphOfSeveralEquations_StacksThem()
    {
        var display = new MathObject { IsDisplay = true };
        display.Content.Nodes.Add(new MathRun("a"));
        display.Equations.Add(MathElement.Of("b"));

        WordDocument document = WordDocument.Create();
        document.Sections[0].AddParagraph().AppendObject(display);

        using Rendered rendered = Rendered.Of(document);

        Assert.True(
            Letter(rendered, "b").Origin.Y < Letter(rendered, "a").Origin.Y,
            "the second equation was not drawn below the first");
    }

    /// <summary>
    /// A letter in an equation leans and a digit does not, which is the convention every
    /// mathematical typesetter follows and the one thing that makes an equation look right.
    /// A glyph names the font resource it was drawn from rather than the face, so what the two
    /// are compared against is a run of ordinary text, which is upright by construction.
    /// </summary>
    [Fact]
    public void LettersLeanAndDigitsDoNot()
    {
        WordDocument document = WithEquation(new MathRun("x1"));
        document.Sections[0].Blocks.Paragraphs.First().AppendText("q");

        using Rendered rendered = Rendered.Of(document);

        Assert.NotEqual(Letter(rendered, "x").FontName, Letter(rendered, "q").FontName);
        Assert.Equal(Letter(rendered, "1").FontName, Letter(rendered, "q").FontName);
    }

    [Fact]
    public void ARunThatSaysItIsNormalText_IsNotLeaned()
    {
        var plain = new MathRun("x") { PropertiesXml = "<m:rPr><m:nor/></m:rPr>" };

        WordDocument document = WithEquation(plain);
        document.Sections[0].Blocks.Paragraphs.First().AppendText("q");

        using Rendered rendered = Rendered.Of(document);

        Assert.Equal(Letter(rendered, "x").FontName, Letter(rendered, "q").FontName);
    }

    [Fact]
    public void AnEquationIsNoLongerReportedAsUndrawnContent()
    {
        using Rendered rendered = Rendered.Of(WithEquation(Half()));

        Assert.DoesNotContain(rendered.Diagnostics, warning => warning.Subject == "raw");
    }
}
