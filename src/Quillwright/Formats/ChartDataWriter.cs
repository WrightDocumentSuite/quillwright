using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Quillwright.Model;

namespace Quillwright.Formats;

/// <summary>
/// Rewrites the data of a chart part (ISO/IEC 29500-1 §21.2): each series' name, categories,
/// values and bubble sizes, in place, leaving everything about how the chart looks alone.
/// </summary>
/// <remarks>
/// The new data is written as literals (<c>c:strLit</c>, <c>c:numLit</c>, a plain
/// <c>c:v</c> for the name) rather than as a cache under a workbook reference: after this, the
/// chart's data lives in the chart, and the formula that pointed into the embedded workbook is
/// gone from the rewritten stretch. Anything else a reference held — the workbook itself, the
/// formatting, the axes — is untouched.
/// </remarks>
internal static class ChartDataWriter
{
    /// <summary>Chart groups whose series plot against two value axes rather than categories.</summary>
    private static readonly string[] TwoValueAxes = ["scatterChart", "bubbleChart"];

    /// <summary>Rewrites the part.</summary>
    /// <param name="content">The chart part as loaded.</param>
    /// <param name="series">One entry per series the chart draws, in document order.</param>
    public static byte[] Rewrite(byte[] content, IReadOnlyList<ChartSeries> series)
    {
        XDocument document;
        using (var reader = XmlReader.Create(new MemoryStream(content), Xml.XmlDefaults.ReaderSettings))
        {
            document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        }

        XNamespace ns = document.Root?.Name.Namespace ?? XNamespace.None;
        List<XElement> existing = [.. document.Descendants(ns + "ser")];
        if (existing.Count != series.Count)
        {
            throw new ArgumentException(
                $"The chart draws {existing.Count} series and {series.Count} were given; adding or removing " +
                "a series changes the chart's structure, which stays the author's job.",
                nameof(series));
        }

        for (int i = 0; i < existing.Count; i++)
            RewriteSeries(existing[i], series[i], ns);

        var buffer = new MemoryStream();
        var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), CloseOutput = false };
        using (var writer = XmlWriter.Create(buffer, settings))
        {
            document.Save(writer);
        }

        return buffer.ToArray();
    }

    private static void RewriteSeries(XElement ser, ChartSeries data, XNamespace ns)
    {
        bool twoAxes = TwoValueAxes.Contains(ser.Parent?.Name.LocalName);

        if (data.Name is not null)
        {
            XElement tx = Ensure(ser, ns + "tx", after: [ns + "order", ns + "idx"]);
            tx.RemoveNodes();
            tx.Add(new XElement(ns + "v", data.Name));
        }

        XName catName = twoAxes ? ns + "xVal" : ns + "cat";
        XName valName = twoAxes ? ns + "yVal" : ns + "val";

        XElement? categories = ser.Element(ns + "cat") ?? ser.Element(ns + "xVal");
        if (data.Categories.Count == 0)
        {
            // No categories means the default ones — 1, 2, 3 — which is what an absent element says.
            categories?.Remove();
        }
        else
        {
            categories ??= EnsureBefore(ser, catName, before: [ns + "val", ns + "yVal"]);
            categories.RemoveNodes();
            categories.Add(CategoryLiteral(ns, data.Categories));
        }

        XElement values = ser.Element(ns + "val") ?? ser.Element(ns + "yVal")
            ?? Ensure(ser, valName, after: [ns + "cat", ns + "xVal", ns + "tx", ns + "order", ns + "idx"]);
        values.RemoveNodes();
        values.Add(NumberLiteral(ns, data.Values));

        if (data.BubbleSizes.Count > 0)
        {
            XElement sizes = Ensure(ser, ns + "bubbleSize", after: [ns + "yVal", ns + "val"]);
            sizes.RemoveNodes();
            sizes.Add(NumberLiteral(ns, data.BubbleSizes));
        }
    }

    /// <summary>
    /// Categories go back as strings unless every one of them is a number, which keeps a value
    /// x-axis — a scatter's, a date axis fed serial numbers — a value axis.
    /// </summary>
    private static XElement CategoryLiteral(XNamespace ns, IReadOnlyList<string> categories)
    {
        var numbers = new double?[categories.Count];
        bool numeric = true;
        for (int i = 0; i < categories.Count && numeric; i++)
        {
            if (double.TryParse(categories[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                numbers[i] = number;
            else
                numeric = false;
        }

        if (numeric)
            return NumberLiteral(ns, numbers);

        var literal = new XElement(
            ns + "strLit",
            new XElement(ns + "ptCount", new XAttribute("val", categories.Count)));
        for (int i = 0; i < categories.Count; i++)
            literal.Add(new XElement(ns + "pt", new XAttribute("idx", i), new XElement(ns + "v", categories[i])));

        return literal;
    }

    private static XElement NumberLiteral(XNamespace ns, IReadOnlyList<double?> values)
    {
        var literal = new XElement(
            ns + "numLit",
            new XElement(ns + "formatCode", "General"),
            new XElement(ns + "ptCount", new XAttribute("val", values.Count)));
        for (int i = 0; i < values.Count; i++)
        {
            // A gap in the data is a point that is not there, which is how a cache says it too.
            if (values[i] is { } value)
            {
                literal.Add(new XElement(
                    ns + "pt",
                    new XAttribute("idx", i),
                    new XElement(ns + "v", value.ToString("R", CultureInfo.InvariantCulture))));
            }
        }

        return literal;
    }

    /// <summary>The child, created behind the last of the named anchors present if it was missing.</summary>
    private static XElement Ensure(XElement parent, XName name, XName[] after)
    {
        if (parent.Element(name) is { } present)
            return present;

        var created = new XElement(name);
        foreach (XName anchor in after)
        {
            if (parent.Element(anchor) is { } sibling)
            {
                sibling.AddAfterSelf(created);
                return created;
            }
        }

        parent.AddFirst(created);
        return created;
    }

    /// <summary>The child, created ahead of the first of the named followers present if it was missing.</summary>
    private static XElement EnsureBefore(XElement parent, XName name, XName[] before)
    {
        if (parent.Element(name) is { } present)
            return present;

        var created = new XElement(name);
        foreach (XName follower in before)
        {
            if (parent.Element(follower) is { } sibling)
            {
                sibling.AddBeforeSelf(created);
                return created;
            }
        }

        parent.Add(created);
        return created;
    }
}
