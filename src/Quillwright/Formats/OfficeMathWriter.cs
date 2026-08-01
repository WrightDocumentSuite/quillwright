using Quillwright.Model;
using Quillwright.Xml;

namespace Quillwright.Formats;

/// <summary>
/// Writes the equation tree back as Office Math (ISO/IEC 29500-1 §22.1).
/// </summary>
/// <remarks>
/// An equation that arrived in a file and was not edited is written back as the bytes it
/// arrived as, which is the only way the parts of §22.1 the model does not hold — the
/// spacing, the breaking, the fonts — survive a round trip. Markup is generated only for an
/// equation a caller built or changed.
/// </remarks>
internal static partial class OfficeMathWriter
{
    public static void Write(Utf8XmlWriter writer, MathObject equation)
    {
        if (!equation.IsDirty && equation.OriginalXml is { } original)
        {
            writer.WriteRawXml(original);
            return;
        }

        if (equation.IsDisplay)
        {
            writer.WriteRaw("<m:oMathPara>"u8);
            WriteJustification(writer, equation.Justification);
        }

        foreach (MathElement content in equation.Equations)
        {
            writer.WriteRaw("<m:oMath>"u8);
            WriteNodes(writer, content);
            writer.WriteRaw("</m:oMath>"u8);
        }

        // A display paragraph must hold at least one equation, whatever the model was left in.
        if (equation.Equations.Count == 0)
            writer.WriteRaw("<m:oMath/>"u8);

        if (equation.IsDisplay)
            writer.WriteRaw("</m:oMathPara>"u8);
    }

    /// <summary>Writes the paragraph properties, which hold nothing but the justification.</summary>
    private static void WriteJustification(Utf8XmlWriter writer, MathJustification justification)
    {
        if (justification == MathJustification.Default)
            return;

        writer.WriteRaw("<m:oMathParaPr><m:jc m:val=\""u8);
        writer.WriteRaw(justification switch
        {
            MathJustification.Left => "left"u8,
            MathJustification.Right => "right"u8,
            MathJustification.Center => "center"u8,
            _ => "centerGroup"u8,
        });

        writer.WriteRaw("\"/></m:oMathParaPr>"u8);
    }

    private static void WriteNodes(Utf8XmlWriter writer, MathElement element)
    {
        foreach (MathNode node in element.Nodes)
            WriteNode(writer, node);
    }

    /// <summary>Writes an argument, which every object wraps its parts in.</summary>
    private static void WriteArgument(Utf8XmlWriter writer, ReadOnlySpan<byte> name, MathElement element)
    {
        writer.WriteRaw("<m:"u8);
        writer.WriteRaw(name);
        writer.WriteRaw(">"u8);
        WriteNodes(writer, element);
        writer.WriteRaw("</m:"u8);
        writer.WriteRaw(name);
        writer.WriteRaw(">"u8);
    }

    private static void WriteNode(Utf8XmlWriter writer, MathNode node)
    {
        switch (node)
        {
            case MathRun run:
                WriteRun(writer, run);
                return;
            case RawMath raw:
                writer.WriteRawXml(raw.Xml);
                return;
            case MathFraction fraction:
                WriteFraction(writer, fraction);
                return;
            case MathRadical radical:
                WriteRadical(writer, radical);
                return;
            case MathScript script:
                WriteScript(writer, script);
                return;
            case MathNary nary:
                WriteNary(writer, nary);
                return;
            case MathDelimiter delimiter:
                WriteDelimiter(writer, delimiter);
                return;
            case MathFunction function:
                writer.WriteRaw("<m:func>"u8);
                WriteControlOnly(writer, "funcPr"u8, function);
                WriteArgument(writer, "fName"u8, function.Name);
                WriteArgument(writer, "e"u8, function.Argument);
                writer.WriteRaw("</m:func>"u8);
                return;
            case MathMatrix matrix:
                WriteMatrix(writer, matrix);
                return;
            case MathBar bar:
                WriteWrapper(writer, "bar"u8, "barPr"u8, "pos"u8, Name(bar.Position), bar, bar.Base);
                return;
            case MathAccent accent:
                WriteWrapper(writer, "acc"u8, "accPr"u8, "chr"u8, accent.Character, accent, accent.Base);
                return;
            case MathGroupCharacter group:
                WriteGroupCharacter(writer, group);
                return;
            case MathBox box:
                writer.WriteRaw("<m:box>"u8);
                WriteControlOnly(writer, "boxPr"u8, box);
                WriteArgument(writer, "e"u8, box.Base);
                writer.WriteRaw("</m:box>"u8);
                return;
            case MathBorderBox borderBox:
                WriteBorderBox(writer, borderBox);
                return;
            case MathArray array:
                WriteArray(writer, array);
                return;
            case MathLimit limit:
                WriteLimit(writer, limit);
                return;
            case MathPhantom phantom:
                WritePhantom(writer, phantom);
                return;
        }
    }

    /// <summary>Opens a properties element.</summary>
    private static void OpenProperties(Utf8XmlWriter writer, ReadOnlySpan<byte> name)
    {
        writer.WriteRaw("<m:"u8);
        writer.WriteRaw(name);
        writer.WriteRaw(">"u8);
    }

    /// <summary>
    /// Closes a properties element, writing the control properties the object arrived with
    /// last, which is where every one of them belongs in the schema.
    /// </summary>
    private static void CloseProperties(Utf8XmlWriter writer, ReadOnlySpan<byte> name, MathNode node)
    {
        RawXml.Write(writer, node.ControlPropertiesXml);
        writer.WriteRaw("</m:"u8);
        writer.WriteRaw(name);
        writer.WriteRaw(">"u8);
    }

    /// <summary>
    /// Writes a properties element holding nothing but the control properties, and nothing at
    /// all when the object has none — an empty one would be noise in every equation.
    /// </summary>
    private static void WriteControlOnly(Utf8XmlWriter writer, ReadOnlySpan<byte> name, MathNode node)
    {
        if (node.ControlPropertiesXml is null)
            return;

        OpenProperties(writer, name);
        CloseProperties(writer, name, node);
    }

    private static void WriteRun(Utf8XmlWriter writer, MathRun run)
    {
        writer.WriteRaw("<m:r>"u8);
        RawXml.Write(writer, run.PropertiesXml);
        writer.WriteRaw("<m:t"u8);
        if (run.Text.Length > 0 && (char.IsWhiteSpace(run.Text[0]) || char.IsWhiteSpace(run.Text[^1])))
            writer.WriteRaw(" xml:space=\"preserve\""u8);
        writer.WriteRaw(">"u8);
        writer.WriteText(run.Text);
        writer.WriteRaw("</m:t></m:r>"u8);
    }

    private static void WriteFraction(Utf8XmlWriter writer, MathFraction fraction)
    {
        writer.WriteRaw("<m:f>"u8);
        if (fraction.Kind != MathFractionKind.Bar || fraction.ControlPropertiesXml is not null)
        {
            OpenProperties(writer, "fPr"u8);
            if (fraction.Kind != MathFractionKind.Bar)
            {
                writer.WriteRaw("<m:type m:val=\""u8);
                writer.WriteRaw(Name(fraction.Kind));
                writer.WriteRaw("\"/>"u8);
            }

            CloseProperties(writer, "fPr"u8, fraction);
        }

        WriteArgument(writer, "num"u8, fraction.Numerator);
        WriteArgument(writer, "den"u8, fraction.Denominator);
        writer.WriteRaw("</m:f>"u8);
    }

    private static void WriteRadical(Utf8XmlWriter writer, MathRadical radical)
    {
        writer.WriteRaw("<m:rad>"u8);
        bool hide = radical.HideDegree || radical.Degree.IsEmpty;
        if (hide || radical.ControlPropertiesXml is not null)
        {
            OpenProperties(writer, "radPr"u8);
            if (hide)
                writer.WriteRaw("<m:degHide m:val=\"1\"/>"u8);

            CloseProperties(writer, "radPr"u8, radical);
        }

        WriteArgument(writer, "deg"u8, radical.Degree);
        WriteArgument(writer, "e"u8, radical.Base);
        writer.WriteRaw("</m:rad>"u8);
    }

    /// <summary>
    /// The four script elements differ only in which parts they carry, so which one to write
    /// follows from the tree rather than from anything stored.
    /// </summary>
    private static void WriteScript(Utf8XmlWriter writer, MathScript script)
    {
        bool sub = !script.Subscript.IsEmpty;
        bool sup = !script.Superscript.IsEmpty;
        ReadOnlySpan<byte> name = script.Placement == MathScriptPlacement.Before
            ? "sPre"u8
            : sub && sup ? "sSubSup"u8 : sub ? "sSub"u8 : "sSup"u8;

        writer.WriteRaw("<m:"u8);
        writer.WriteRaw(name);
        writer.WriteRaw(">"u8);

        if (script.ControlPropertiesXml is not null)
        {
            // The properties element of a script is its own name with "Pr" on the end.
            Span<byte> properties = stackalloc byte[name.Length + 2];
            name.CopyTo(properties);
            "Pr"u8.CopyTo(properties[name.Length..]);
            WriteControlOnly(writer, properties, script);
        }

        // A pre-script puts its base last; every other form puts it first.
        if (script.Placement != MathScriptPlacement.Before)
            WriteArgument(writer, "e"u8, script.Base);
        if (sub || script.Placement == MathScriptPlacement.Before)
            WriteArgument(writer, "sub"u8, script.Subscript);
        if (sup || script.Placement == MathScriptPlacement.Before)
            WriteArgument(writer, "sup"u8, script.Superscript);
        if (script.Placement == MathScriptPlacement.Before)
            WriteArgument(writer, "e"u8, script.Base);

        writer.WriteRaw("</m:"u8);
        writer.WriteRaw(name);
        writer.WriteRaw(">"u8);
    }

    private static void WriteNary(Utf8XmlWriter writer, MathNary nary)
    {
        writer.WriteRaw("<m:nary><m:naryPr><m:chr m:val=\""u8);
        writer.WriteAttributeText(nary.Operator);
        writer.WriteRaw("\"/>"u8);
        if (nary.HideLower)
            writer.WriteRaw("<m:subHide m:val=\"1\"/>"u8);
        if (nary.HideUpper)
            writer.WriteRaw("<m:supHide m:val=\"1\"/>"u8);
        CloseProperties(writer, "naryPr"u8, nary);

        WriteArgument(writer, "sub"u8, nary.Lower);
        WriteArgument(writer, "sup"u8, nary.Upper);
        WriteArgument(writer, "e"u8, nary.Base);
        writer.WriteRaw("</m:nary>"u8);
    }

    private static void WriteDelimiter(Utf8XmlWriter writer, MathDelimiter delimiter)
    {
        writer.WriteRaw("<m:d>"u8);
        OpenProperties(writer, "dPr"u8);

        // The schema orders these opening, separator, closing, which is not the order they are
        // read in; writing them any other way produces a file Word offers to repair.
        WriteCharacter(writer, "begChr"u8, delimiter.Begin);
        WriteCharacter(writer, "sepChr"u8, delimiter.Separator);
        WriteCharacter(writer, "endChr"u8, delimiter.End);
        CloseProperties(writer, "dPr"u8, delimiter);

        if (delimiter.Arguments.Count == 0)
            WriteArgument(writer, "e"u8, new MathElement());
        foreach (MathElement argument in delimiter.Arguments)
            WriteArgument(writer, "e"u8, argument);

        writer.WriteRaw("</m:d>"u8);
    }

    private static void WriteMatrix(Utf8XmlWriter writer, MathMatrix matrix)
    {
        writer.WriteRaw("<m:m>"u8);
        WriteControlOnly(writer, "mPr"u8, matrix);
        foreach (MathMatrixRow row in matrix.Rows)
        {
            writer.WriteRaw("<m:mr>"u8);
            foreach (MathElement cell in row.Cells)
                WriteArgument(writer, "e"u8, cell);
            writer.WriteRaw("</m:mr>"u8);
        }

        writer.WriteRaw("</m:m>"u8);
    }

    private static void WriteGroupCharacter(Utf8XmlWriter writer, MathGroupCharacter group)
    {
        writer.WriteRaw("<m:groupChr>"u8);
        OpenProperties(writer, "groupChrPr"u8);
        WriteCharacter(writer, "chr"u8, group.Character);
        writer.WriteRaw("<m:pos m:val=\""u8);
        writer.WriteRaw(Name(group.Position));
        writer.WriteRaw("\"/>"u8);
        CloseProperties(writer, "groupChrPr"u8, group);
        WriteArgument(writer, "e"u8, group.Base);
        writer.WriteRaw("</m:groupChr>"u8);
    }

    /// <summary>An object that is a base plus one property naming a character or a side.</summary>
    private static void WriteWrapper(
        Utf8XmlWriter writer,
        ReadOnlySpan<byte> element,
        ReadOnlySpan<byte> properties,
        ReadOnlySpan<byte> property,
        string value,
        MathNode node,
        MathElement content)
    {
        writer.WriteRaw("<m:"u8);
        writer.WriteRaw(element);
        writer.WriteRaw(">"u8);
        OpenProperties(writer, properties);
        WriteCharacter(writer, property, value);
        CloseProperties(writer, properties, node);
        WriteArgument(writer, "e"u8, content);
        writer.WriteRaw("</m:"u8);
        writer.WriteRaw(element);
        writer.WriteRaw(">"u8);
    }

    private static void WriteWrapper(
        Utf8XmlWriter writer,
        ReadOnlySpan<byte> element,
        ReadOnlySpan<byte> properties,
        ReadOnlySpan<byte> property,
        ReadOnlySpan<byte> value,
        MathNode node,
        MathElement content) =>
        WriteWrapper(writer, element, properties, property, System.Text.Encoding.UTF8.GetString(value), node, content);

    private static void WriteCharacter(Utf8XmlWriter writer, ReadOnlySpan<byte> name, string value)
    {
        if (value.Length == 0)
            return;

        writer.WriteRaw("<m:"u8);
        writer.WriteRaw(name);
        writer.WriteRaw(" m:val=\""u8);
        writer.WriteAttributeText(value);
        writer.WriteRaw("\"/>"u8);
    }

    private static ReadOnlySpan<byte> Name(MathFractionKind kind) => kind switch
    {
        MathFractionKind.Skewed => "skw"u8,
        MathFractionKind.Linear => "lin"u8,
        MathFractionKind.NoBar => "noBar"u8,
        _ => "bar"u8,
    };

    private static ReadOnlySpan<byte> Name(MathEdge edge) => edge == MathEdge.Top ? "top"u8 : "bot"u8;
}
