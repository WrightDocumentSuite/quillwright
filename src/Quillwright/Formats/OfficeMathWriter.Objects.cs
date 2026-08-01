using Quillwright.Model;
using Quillwright.Xml;

namespace Quillwright.Formats;

/// <summary>
/// Writes the four objects that are structure rather than a variation on a script: a framed
/// box, an array of equations, a limit written under or over its base, and a phantom.
/// </summary>
/// <remarks>
/// Every properties element in the vocabulary is a fixed sequence, so each of these writes its
/// toggles in the order the schema declares them and leaves out the ones that are off — a
/// toggle written as <c>0</c> means the same as one left out, and Word writes neither.
/// </remarks>
internal static partial class OfficeMathWriter
{
    private static void WriteBorderBox(Utf8XmlWriter writer, MathBorderBox box)
    {
        writer.WriteRaw("<m:borderBox>"u8);
        if (HasProperties(box))
        {
            OpenProperties(writer, "borderBoxPr"u8);
            WriteToggle(writer, "hideTop"u8, box.HideTop);
            WriteToggle(writer, "hideBot"u8, box.HideBottom);
            WriteToggle(writer, "hideLeft"u8, box.HideLeft);
            WriteToggle(writer, "hideRight"u8, box.HideRight);
            WriteToggle(writer, "strikeH"u8, box.StrikeHorizontal);
            WriteToggle(writer, "strikeV"u8, box.StrikeVertical);
            WriteToggle(writer, "strikeBLTR"u8, box.StrikeUpward);
            WriteToggle(writer, "strikeTLBR"u8, box.StrikeDownward);
            CloseProperties(writer, "borderBoxPr"u8, box);
        }

        WriteArgument(writer, "e"u8, box.Base);
        writer.WriteRaw("</m:borderBox>"u8);
    }

    private static bool HasProperties(MathBorderBox box) =>
        box.ControlPropertiesXml is not null ||
        box.HideTop || box.HideBottom || box.HideLeft || box.HideRight ||
        box.StrikeHorizontal || box.StrikeVertical || box.StrikeUpward || box.StrikeDownward;

    private static void WriteArray(Utf8XmlWriter writer, MathArray array)
    {
        writer.WriteRaw("<m:eqArr>"u8);
        WriteControlOnly(writer, "eqArrPr"u8, array);

        // The schema wants at least one row, and an array with none is a caller's oversight
        // rather than a reason to write a file Word will refuse.
        if (array.Rows.Count == 0)
            WriteArgument(writer, "e"u8, new MathElement());

        foreach (MathElement row in array.Rows)
            WriteArgument(writer, "e"u8, row);

        writer.WriteRaw("</m:eqArr>"u8);
    }

    private static void WriteLimit(Utf8XmlWriter writer, MathLimit limit)
    {
        ReadOnlySpan<byte> element = limit.Position == MathEdge.Top ? "limUpp"u8 : "limLow"u8;
        ReadOnlySpan<byte> properties = limit.Position == MathEdge.Top ? "limUppPr"u8 : "limLowPr"u8;

        writer.WriteRaw("<m:"u8);
        writer.WriteRaw(element);
        writer.WriteRaw(">"u8);
        WriteControlOnly(writer, properties, limit);
        WriteArgument(writer, "e"u8, limit.Base);
        WriteArgument(writer, "lim"u8, limit.Limit);
        writer.WriteRaw("</m:"u8);
        writer.WriteRaw(element);
        writer.WriteRaw(">"u8);
    }

    private static void WritePhantom(Utf8XmlWriter writer, MathPhantom phantom)
    {
        writer.WriteRaw("<m:phant>"u8);
        if (HasProperties(phantom))
        {
            OpenProperties(writer, "phantPr"u8);
            WriteToggle(writer, "show"u8, phantom.Show);
            WriteToggle(writer, "zeroWid"u8, phantom.ZeroWidth);
            WriteToggle(writer, "zeroAsc"u8, phantom.ZeroAscent);
            WriteToggle(writer, "zeroDesc"u8, phantom.ZeroDescent);
            WriteToggle(writer, "transp"u8, phantom.Transparent);
            CloseProperties(writer, "phantPr"u8, phantom);
        }

        WriteArgument(writer, "e"u8, phantom.Base);
        writer.WriteRaw("</m:phant>"u8);
    }

    private static bool HasProperties(MathPhantom phantom) =>
        phantom.ControlPropertiesXml is not null ||
        phantom.Show || phantom.ZeroWidth || phantom.ZeroAscent || phantom.ZeroDescent || phantom.Transparent;

    /// <summary>Writes a property whose presence is its value, and nothing when it is off.</summary>
    private static void WriteToggle(Utf8XmlWriter writer, ReadOnlySpan<byte> name, bool on)
    {
        if (!on)
            return;

        writer.WriteRaw("<m:"u8);
        writer.WriteRaw(name);
        writer.WriteRaw(" m:val=\"1\"/>"u8);
    }
}
