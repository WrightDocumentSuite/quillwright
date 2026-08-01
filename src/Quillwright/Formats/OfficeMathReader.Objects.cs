using System.Xml;
using Quillwright.Model;

namespace Quillwright.Formats;

/// <summary>
/// The four Office Math objects that are structure rather than a variation on a script: a
/// framed box, an array of equations, a limit written under or over its base, and a phantom.
/// </summary>
internal static partial class OfficeMathReader
{
    /// <summary>Reads a framed box (<c>m:borderBox</c>, §22.1.2.11).</summary>
    private static MathBorderBox BorderBox(XmlReader xml)
    {
        var box = new MathBorderBox();
        XmlHelp.ForEachChild(xml, (reader, name) =>
        {
            switch (name)
            {
                case "e": ReadNodes(reader, box.Base); return;
                case "borderBoxPr": ReadBorderBoxProperties(reader, box); return;
                default: reader.Skip(); return;
            }
        });

        return box;
    }

    /// <summary>
    /// Reads which edges of a frame are left out and which lines are drawn across it. Every one
    /// of them is a toggle, so the whole element reads as eight of the same thing.
    /// </summary>
    private static void ReadBorderBoxProperties(XmlReader xml, MathBorderBox box) =>
        ReadProperties(xml, box, (reader, name) =>
        {
            bool on = Toggle(Value(reader) ?? string.Empty);
            switch (name)
            {
                case "hideTop": box.HideTop = on; break;
                case "hideBot": box.HideBottom = on; break;
                case "hideLeft": box.HideLeft = on; break;
                case "hideRight": box.HideRight = on; break;
                case "strikeH": box.StrikeHorizontal = on; break;
                case "strikeV": box.StrikeVertical = on; break;
                case "strikeBLTR": box.StrikeUpward = on; break;
                case "strikeTLBR": box.StrikeDownward = on; break;
            }

            reader.Skip();
        });

    /// <summary>Reads a stack of aligned equations (<c>m:eqArr</c>, §22.1.2.34).</summary>
    private static MathArray Array(XmlReader xml)
    {
        var array = new MathArray();
        XmlHelp.ForEachChild(xml, (reader, name) =>
        {
            switch (name)
            {
                case "e":
                    var row = new MathElement();
                    ReadNodes(reader, row);
                    array.Rows.Add(row);
                    return;
                case "eqArrPr":
                    ReadProperties(reader, array, Ignore);
                    return;
                default:
                    reader.Skip();
                    return;
            }
        });

        return array;
    }

    /// <summary>Reads a limit written under or over its base (<c>m:limLow</c>, <c>m:limUpp</c>).</summary>
    /// <param name="xml">Reader positioned on the object.</param>
    /// <param name="position">Which of the two elements it is.</param>
    private static MathLimit Limit(XmlReader xml, MathEdge position)
    {
        var limit = new MathLimit { Position = position };
        XmlHelp.ForEachChild(xml, (reader, name) =>
        {
            switch (name)
            {
                case "e": ReadNodes(reader, limit.Base); return;
                case "lim": ReadNodes(reader, limit.Limit); return;
                case "limLowPr" or "limUppPr": ReadProperties(reader, limit, Ignore); return;
                default: reader.Skip(); return;
            }
        });

        return limit;
    }

    /// <summary>Reads a phantom (<c>m:phant</c>, §22.1.2.81).</summary>
    private static MathPhantom Phantom(XmlReader xml)
    {
        var phantom = new MathPhantom();
        XmlHelp.ForEachChild(xml, (reader, name) =>
        {
            switch (name)
            {
                case "e": ReadNodes(reader, phantom.Base); return;
                case "phantPr": ReadPhantomProperties(reader, phantom); return;
                default: reader.Skip(); return;
            }
        });

        return phantom;
    }

    /// <summary>Reads what a phantom takes room for and whether any of it is drawn.</summary>
    private static void ReadPhantomProperties(XmlReader xml, MathPhantom phantom) =>
        ReadProperties(xml, phantom, (reader, name) =>
        {
            bool on = Toggle(Value(reader) ?? string.Empty);
            switch (name)
            {
                case "show": phantom.Show = on; break;
                case "zeroWid": phantom.ZeroWidth = on; break;
                case "zeroAsc": phantom.ZeroAscent = on; break;
                case "zeroDesc": phantom.ZeroDescent = on; break;
                case "transp": phantom.Transparent = on; break;
            }

            reader.Skip();
        });
}
