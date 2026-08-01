using System.Globalization;
using System.Xml;
using Quillwright.Model;

namespace Quillwright.Formats;

/// <summary>
/// Reads a chart part (<c>word/charts/chart1.xml</c>, ISO/IEC 29500-1 §21.2) into what it
/// draws: a kind, a title, and the series with the numbers the file cached for them.
/// </summary>
/// <remarks>
/// Elements are matched by local name alone. A chart part is a vocabulary of its own with
/// nothing to confuse it with, and both spellings of the namespace — the Transitional one and
/// the Strict one a package from an ISO-conformant producer uses — appear in the wild.
/// </remarks>
internal static class ChartPartReader
{
    /// <summary>Reads the whole part.</summary>
    /// <param name="xml">A reader over the part.</param>
    /// <param name="partPath">Absolute name of the part, for the caller to find it again.</param>
    public static Chart Read(XmlReader xml, string partPath)
    {
        var kind = ChartKind.Unknown;
        string? title = null;
        var series = new List<ChartSeries>();

        while (xml.Read())
        {
            if (xml.NodeType != XmlNodeType.Element)
                continue;

            if (xml.LocalName == "title")
                title ??= ReadTitle(xml);
            else if (xml.LocalName == "ser")
                series.Add(ReadSeries(xml));
            else if (kind == ChartKind.Unknown && ParseKind(xml.LocalName) is { } found)
                kind = found;
        }

        return new Chart { Location = partPath, Kind = kind, Title = title, Series = series };
    }

    /// <summary>The words of a title, wherever in its rich text they sit.</summary>
    private static string? ReadTitle(XmlReader xml)
    {
        var text = new System.Text.StringBuilder();
        using XmlReader subtree = xml.ReadSubtree();
        bool advance = true;
        while (!advance || subtree.Read())
        {
            advance = true;
            if (subtree.NodeType == XmlNodeType.Element && subtree.LocalName is "t" or "v")
            {
                // Reading the content leaves the reader on the node after the element, which
                // the loop must see rather than step over.
                text.Append(subtree.ReadElementContentAsString());
                advance = false;
            }
        }

        return text.Length == 0 ? null : text.ToString();
    }

    /// <summary>
    /// One series: its name, its categories and its values, each of which is a cache of
    /// points under a reference to where they came from.
    /// </summary>
    private static ChartSeries ReadSeries(XmlReader xml)
    {
        string? name = null;
        var categories = new List<string>();
        var values = new List<double?>();
        string section = string.Empty;
        int depth = -1;

        using XmlReader subtree = xml.ReadSubtree();
        bool advance = true;
        while (!advance || subtree.Read())
        {
            advance = true;
            if (subtree.NodeType == XmlNodeType.EndElement && depth >= 0 && subtree.Depth <= depth)
            {
                section = string.Empty;
                depth = -1;
                continue;
            }

            if (subtree.NodeType != XmlNodeType.Element)
                continue;

            if (depth < 0 && subtree.LocalName is "tx" or "cat" or "val" or "xVal" or "yVal")
            {
                section = subtree.LocalName;
                depth = subtree.Depth;
            }
            else if (subtree.LocalName is "v" or "t")
            {
                // Reading the content leaves the reader on the node after the element — often
                // the end of the section itself, which the loop must see rather than step over.
                Collect(section, subtree.ReadElementContentAsString(), ref name, categories, values);
                advance = false;
            }
        }

        return new ChartSeries { Name = name, Categories = categories, Values = values };
    }

    private static void Collect(string section, string text, ref string? name, List<string> categories, List<double?> values)
    {
        switch (section)
        {
            case "tx":
                name ??= text;
                return;
            case "cat" or "xVal":
                categories.Add(text);
                return;
            case "val" or "yVal":
                values.Add(double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                    ? value
                    : null);
                return;
        }
    }

    /// <summary>The kind a plot-area element names, or nothing when it names something else.</summary>
    private static ChartKind? ParseKind(string element) => element switch
    {
        "barChart" or "bar3DChart" => ChartKind.Bar,
        "lineChart" or "line3DChart" => ChartKind.Line,
        "pieChart" or "pie3DChart" or "ofPieChart" => ChartKind.Pie,
        "doughnutChart" => ChartKind.Doughnut,
        "areaChart" or "area3DChart" => ChartKind.Area,
        "scatterChart" => ChartKind.Scatter,
        "bubbleChart" => ChartKind.Bubble,
        "radarChart" => ChartKind.Radar,
        "surfaceChart" or "surface3DChart" => ChartKind.Surface,
        "stockChart" => ChartKind.Stock,
        _ => null,
    };
}
