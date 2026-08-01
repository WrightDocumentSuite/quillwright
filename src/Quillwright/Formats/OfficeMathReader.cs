using System.Text;
using System.Xml;
using Quillwright.Model;

namespace Quillwright.Formats;

/// <summary>
/// Reads the Office Math vocabulary (ISO/IEC 29500-1 §22.1) into the equation tree.
/// </summary>
/// <remarks>
/// §22.1 has 124 elements and most of them say how a formula is drawn. What is modelled here
/// is the structure: which part is a numerator, which is an exponent, what the sum runs over.
/// An element outside that set becomes a <see cref="RawMath"/> holding its own bytes, so the
/// markup survives whether or not the model understands it.
/// </remarks>
internal static partial class OfficeMathReader
{
    /// <summary>Reads an equation, or returns <see langword="null"/> when the markup is not one.</summary>
    /// <param name="markup">The verbatim <c>m:oMath</c> or <c>m:oMathPara</c> fragment.</param>
    public static MathObject? Parse(string markup)
    {
        try
        {
            using XmlReader xml = XmlReader.Create(new StringReader(markup), Xml.XmlDefaults.ReaderSettings);
            if (!xml.MoveToContent().Equals(XmlNodeType.Element) || !DocxSchema.IsMathNamespace(xml.NamespaceURI))
                return null;

            return xml.LocalName switch
            {
                "oMath" => Equation(xml, markup, display: false),
                "oMathPara" => Paragraph(xml, markup),
                _ => null,
            };
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static MathObject Equation(XmlReader xml, string markup, bool display)
    {
        var equation = new MathObject { OriginalXml = markup, IsDisplay = display };
        ReadNodes(xml, equation.Content);
        return equation;
    }

    /// <summary>
    /// A display equation, which is a paragraph of its own holding one equation or several,
    /// with a justification that applies to all of them.
    /// </summary>
    private static MathObject Paragraph(XmlReader xml, string markup)
    {
        var paragraph = new MathObject { OriginalXml = markup, IsDisplay = true };
        paragraph.Equations.Clear();

        XmlHelp.ForEachChild(xml, (reader, name) =>
        {
            if (!DocxSchema.IsMathNamespace(reader.NamespaceURI))
            {
                reader.Skip();
                return;
            }

            switch (name)
            {
                case "oMath":
                    var content = new MathElement();
                    ReadNodes(reader, content);
                    paragraph.Equations.Add(content);
                    return;
                case "oMathParaPr":
                    paragraph.Justification = Justification(Property(reader, "jc"));
                    return;
                default:
                    reader.Skip();
                    return;
            }
        });

        return paragraph;
    }

    /// <summary>Reads the children of a container into an argument.</summary>
    private static void ReadNodes(XmlReader xml, MathElement into) =>
        XmlHelp.ForEachChild(xml, (reader, name) =>
        {
            if (!DocxSchema.IsMathNamespace(reader.NamespaceURI))
            {
                into.Nodes.Add(Preserve(reader));
                return;
            }

            if (Node(reader, name) is { } node)
                into.Nodes.Add(node);
        });

    private static MathNode? Node(XmlReader xml, string name) => name switch
    {
        "r" => Run(xml),
        "f" => Fraction(xml),
        "rad" => Radical(xml),
        "sSub" or "sSup" or "sSubSup" or "sPre" => Script(xml, name),
        "nary" => Nary(xml),
        "d" => Delimiter(xml),
        "func" => Function(xml),
        "m" => Matrix(xml),
        "bar" => Bar(xml),
        "acc" => Accent(xml),
        "groupChr" => GroupCharacter(xml),
        "box" => Box(xml),
        "borderBox" => BorderBox(xml),
        "eqArr" => Array(xml),
        "limLow" or "limUpp" => Limit(xml, name == "limUpp" ? MathEdge.Top : MathEdge.Bottom),
        "phant" => Phantom(xml),

        // The properties of the equation itself, and the control run that carries the
        // formatting of the object around it; neither is content.
        "oMathParaPr" or "ctrlPr" => Preserve(xml),
        _ => Preserve(xml),
    };

    /// <summary>Keeps an element this does not model, with whatever text is inside it.</summary>
    private static RawMath Preserve(XmlReader xml)
    {
        string markup = xml.ReadOuterXml();
        return new RawMath(markup, TextOf(markup));
    }

    private static MathRun Run(XmlReader xml)
    {
        var run = new MathRun();
        var text = new StringBuilder();
        var properties = new StringBuilder();

        XmlHelp.ForEachChild(xml, (reader, name) =>
        {
            switch (name)
            {
                case "t" when DocxSchema.IsMathNamespace(reader.NamespaceURI):
                    text.Append(reader.ReadElementContentAsString());
                    return;
                case "rPr":
                    properties.Append(reader.ReadOuterXml());
                    return;
                default:
                    properties.Append(reader.ReadOuterXml());
                    return;
            }
        });

        run.Text = text.ToString();
        if (properties.Length > 0)
            run.PropertiesXml = properties.ToString();
        return run;
    }

    private static MathFraction Fraction(XmlReader xml)
    {
        var fraction = new MathFraction();
        XmlHelp.ForEachChild(xml, (reader, name) =>
        {
            switch (name)
            {
                case "num": ReadNodes(reader, fraction.Numerator); return;
                case "den": ReadNodes(reader, fraction.Denominator); return;
                case "fPr": fraction.Kind = FractionKind(Property(reader, fraction, "type")); return;
                default: reader.Skip(); return;
            }
        });

        return fraction;
    }

    private static MathRadical Radical(XmlReader xml)
    {
        var radical = new MathRadical();
        XmlHelp.ForEachChild(xml, (reader, name) =>
        {
            switch (name)
            {
                case "deg": ReadNodes(reader, radical.Degree); return;
                case "e": ReadNodes(reader, radical.Base); return;
                case "radPr": radical.HideDegree = Toggle(Property(reader, radical, "degHide")); return;
                default: reader.Skip(); return;
            }
        });

        return radical;
    }

    private static MathScript Script(XmlReader xml, string name)
    {
        var script = new MathScript
        {
            Placement = name == "sPre" ? MathScriptPlacement.Before : MathScriptPlacement.After,
        };

        XmlHelp.ForEachChild(xml, (reader, child) =>
        {
            switch (child)
            {
                case "e": ReadNodes(reader, script.Base); return;
                case "sub": ReadNodes(reader, script.Subscript); return;
                case "sup": ReadNodes(reader, script.Superscript); return;
                case "sSubPr" or "sSupPr" or "sSubSupPr" or "sPrePr": ReadProperties(reader, script, Ignore); return;
                default: reader.Skip(); return;
            }
        });

        return script;
    }

    private static MathNary Nary(XmlReader xml)
    {
        var nary = new MathNary();
        XmlHelp.ForEachChild(xml, (reader, name) =>
        {
            switch (name)
            {
                case "e": ReadNodes(reader, nary.Base); return;
                case "sub": ReadNodes(reader, nary.Lower); return;
                case "sup": ReadNodes(reader, nary.Upper); return;
                case "naryPr": ReadNaryProperties(reader, nary); return;
                default: reader.Skip(); return;
            }
        });

        return nary;
    }

    private static void ReadNaryProperties(XmlReader xml, MathNary nary) =>
        ReadProperties(xml, nary, (reader, name) =>
        {
            switch (name)
            {
                case "chr": nary.Operator = Value(reader) ?? nary.Operator; reader.Skip(); return;
                case "subHide": nary.HideLower = Toggle(Value(reader)); reader.Skip(); return;
                case "supHide": nary.HideUpper = Toggle(Value(reader)); reader.Skip(); return;
                default: reader.Skip(); return;
            }
        });

    private static MathDelimiter Delimiter(XmlReader xml)
    {
        var delimiter = new MathDelimiter();
        XmlHelp.ForEachChild(xml, (reader, name) =>
        {
            switch (name)
            {
                case "e":
                    var argument = new MathElement();
                    ReadNodes(reader, argument);
                    delimiter.Arguments.Add(argument);
                    return;
                case "dPr":
                    ReadDelimiterProperties(reader, delimiter);
                    return;
                default:
                    reader.Skip();
                    return;
            }
        });

        return delimiter;
    }

    private static void ReadDelimiterProperties(XmlReader xml, MathDelimiter delimiter) =>
        ReadProperties(xml, delimiter, (reader, name) =>
        {
            switch (name)
            {
                case "begChr": delimiter.Begin = Value(reader) ?? string.Empty; reader.Skip(); return;
                case "endChr": delimiter.End = Value(reader) ?? string.Empty; reader.Skip(); return;
                case "sepChr": delimiter.Separator = Value(reader) ?? string.Empty; reader.Skip(); return;
                default: reader.Skip(); return;
            }
        });

    private static MathFunction Function(XmlReader xml)
    {
        var function = new MathFunction();
        XmlHelp.ForEachChild(xml, (reader, name) =>
        {
            switch (name)
            {
                case "fName": ReadNodes(reader, function.Name); return;
                case "e": ReadNodes(reader, function.Argument); return;
                case "funcPr": ReadProperties(reader, function, Ignore); return;
                default: reader.Skip(); return;
            }
        });

        return function;
    }

    private static MathMatrix Matrix(XmlReader xml)
    {
        var matrix = new MathMatrix();
        XmlHelp.ForEachChild(xml, (reader, name) =>
        {
            if (name == "mPr")
            {
                ReadProperties(reader, matrix, Ignore);
                return;
            }

            if (name != "mr")
            {
                reader.Skip();
                return;
            }

            var row = new MathMatrixRow();
            XmlHelp.ForEachChild(reader, (cell, child) =>
            {
                if (child != "e")
                {
                    cell.Skip();
                    return;
                }

                var content = new MathElement();
                ReadNodes(cell, content);
                row.Cells.Add(content);
            });

            matrix.Rows.Add(row);
        });

        return matrix;
    }

    private static MathBar Bar(XmlReader xml)
    {
        var bar = new MathBar();
        XmlHelp.ForEachChild(xml, (reader, name) =>
        {
            switch (name)
            {
                case "e": ReadNodes(reader, bar.Base); return;
                case "barPr": bar.Position = Edge(Property(reader, bar, "pos")); return;
                default: reader.Skip(); return;
            }
        });

        return bar;
    }

    private static MathAccent Accent(XmlReader xml)
    {
        var accent = new MathAccent();
        XmlHelp.ForEachChild(xml, (reader, name) =>
        {
            switch (name)
            {
                case "e": ReadNodes(reader, accent.Base); return;
                case "accPr": accent.Character = Property(reader, accent, "chr") ?? accent.Character; return;
                default: reader.Skip(); return;
            }
        });

        return accent;
    }

    private static MathGroupCharacter GroupCharacter(XmlReader xml)
    {
        var group = new MathGroupCharacter();
        XmlHelp.ForEachChild(xml, (reader, name) =>
        {
            switch (name)
            {
                case "e":
                    ReadNodes(reader, group.Base);
                    return;
                case "groupChrPr":
                    string? character = null;
                    string? position = null;
                    ReadProperties(reader, group, (property, child) =>
                    {
                        if (child == "chr")
                            character = Value(property);
                        else if (child == "pos")
                            position = Value(property);
                        property.Skip();
                    });

                    group.Character = character ?? group.Character;
                    group.Position = Edge(position);
                    return;
                default:
                    reader.Skip();
                    return;
            }
        });

        return group;
    }

    private static MathBox Box(XmlReader xml)
    {
        var box = new MathBox();
        XmlHelp.ForEachChild(xml, (reader, name) =>
        {
            if (name == "e")
                ReadNodes(reader, box.Base);
            else if (name == "boxPr")
                ReadProperties(reader, box, Ignore);
            else
                reader.Skip();
        });

        return box;
    }

    /// <summary>
    /// Reads a properties element into a node: whatever that node models, plus the control
    /// properties every one of them ends with.
    /// </summary>
    /// <param name="xml">Reader positioned on the properties element.</param>
    /// <param name="node">The object the properties belong to.</param>
    /// <param name="onProperty">What to do with a property the node models.</param>
    private static void ReadProperties(XmlReader xml, MathNode node, Action<XmlReader, string> onProperty) =>
        XmlHelp.ForEachChild(xml, (reader, name) =>
        {
            if (name == "ctrlPr" && DocxSchema.IsMathNamespace(reader.NamespaceURI))
                node.ControlPropertiesXml = reader.ReadOuterXml();
            else
                onProperty(reader, name);
        });

    /// <summary>A properties element whose only modelled content is the control properties.</summary>
    private static void Ignore(XmlReader xml, string name) => xml.Skip();

    /// <summary>The <c>val</c> of one named child of a properties element.</summary>
    private static string? Property(XmlReader xml, MathNode node, string name)
    {
        string? value = null;
        ReadProperties(xml, node, (reader, child) =>
        {
            if (child == name)
                value = Value(reader) ?? string.Empty;
            reader.Skip();
        });

        return value;
    }

    /// <summary>The <c>val</c> of one named child of a properties element with no node behind it.</summary>
    private static string? Property(XmlReader xml, string name)
    {
        string? value = null;
        XmlHelp.ForEachChild(xml, (reader, child) =>
        {
            if (child == name)
                value = Value(reader) ?? string.Empty;
            reader.Skip();
        });

        return value;
    }

    /// <summary>
    /// The value of an Office Math property. Its attributes are in the math namespace rather
    /// than the WordprocessingML one, which is the difference that makes <c>m:val</c> not
    /// <c>w:val</c>.
    /// </summary>
    private static string? Value(XmlReader xml) =>
        xml.GetAttribute("val", DocxSchema.NsMath)
        ?? xml.GetAttribute("val", DocxSchema.NsMathStrict)
        ?? XmlHelp.Attr(xml, "val");

    /// <summary>A property whose presence is the value, unless it says otherwise.</summary>
    private static bool Toggle(string? value) => value is not null && value is not ("0" or "false" or "off");

    private static MathFractionKind FractionKind(string? value) => value switch
    {
        "skw" => MathFractionKind.Skewed,
        "lin" => MathFractionKind.Linear,
        "noBar" => MathFractionKind.NoBar,
        _ => MathFractionKind.Bar,
    };

    private static MathEdge Edge(string? value) => value == "top" ? MathEdge.Top : MathEdge.Bottom;

    private static MathJustification Justification(string? value) => value switch
    {
        "left" => MathJustification.Left,
        "right" => MathJustification.Right,
        "center" => MathJustification.Center,
        "centerGroup" => MathJustification.CenterGroup,
        _ => MathJustification.Default,
    };

    /// <summary>The text of every run inside a preserved fragment, for reading it as a line.</summary>
    private static string TextOf(string markup)
    {
        var text = new StringBuilder();
        try
        {
            using XmlReader xml = XmlReader.Create(new StringReader(markup), Xml.XmlDefaults.ReaderSettings);
            while (xml.Read())
            {
                if (xml.NodeType == XmlNodeType.Element && xml.LocalName == "t" &&
                    DocxSchema.IsMathNamespace(xml.NamespaceURI))
                    text.Append(xml.ReadElementContentAsString());
            }
        }
        catch (XmlException)
        {
            return string.Empty;
        }

        return text.ToString();
    }
}
