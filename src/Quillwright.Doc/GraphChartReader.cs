using System.Buffers.Binary;
using System.Text;
using Quillwright.IO;
using Quillwright.Model;

namespace Quillwright.Doc;

/// <summary>
/// Decodes the data of a chart in a legacy document ([MS-OGRAPH]).
/// </summary>
/// <remarks>
/// <para>
/// A chart in a <c>.doc</c> is not part of the document at all: it is an embedded Microsoft
/// Graph object, a compound file of its own whose <c>Workbook</c> stream holds the chart in
/// the record format spreadsheets use. Two things in that stream matter here — the data sheet,
/// which is a grid of cells, and the series, each of which says which row or column of that
/// grid it draws.
/// </para>
/// <para>
/// Everything about how the chart looks is left where it is. The object's own bytes are kept
/// whole, so a consumer that understands Microsoft Graph still has everything.
/// </para>
/// </remarks>
internal static class GraphChartReader
{
    private const string WorkbookStream = "Workbook";

    /// <summary>Bytes of a record header: the type, then the length of what follows.</summary>
    private const int HeaderBytes = 4;

    private const ushort RecordNumber = 0x0003;
    private const ushort RecordEndOfFile = 0x000A;
    private const ushort RecordContinue = 0x003C;
    private const ushort RecordLabel = 0x0204;
    private const ushort RecordBeginOfFile = 0x0809;
    private const ushort RecordChart = 0x1002;
    private const ushort RecordSeries = 0x1003;
    private const ushort RecordSeriesText = 0x100D;
    private const ushort RecordSeriesList = 0x1016;
    private const ushort RecordBar = 0x1017;
    private const ushort RecordLine = 0x1018;
    private const ushort RecordPie = 0x1019;
    private const ushort RecordArea = 0x101A;
    private const ushort RecordScatter = 0x101B;
    private const ushort RecordRadar = 0x103E;
    private const ushort RecordSurface = 0x103F;
    private const ushort RecordRadarArea = 0x1040;
    private const ushort RecordBubble = 0x104A;
    private const ushort RecordTrendline = 0x104B;
    private const ushort RecordDataReference = 0x1051;
    private const ushort RecordOrientation = 0x1055;
    private const ushort RecordErrorBar = 0x105B;

    /// <summary>Reads the chart inside an embedded Microsoft Graph object.</summary>
    /// <param name="embedded">The object, as it came out of the pool.</param>
    /// <returns>The chart, or <see langword="null"/> when the object holds nothing readable.</returns>
    public static Chart? Read(EmbeddedObject embedded)
    {
        byte[] content = embedded.Content.ToArray();
        if (!CompoundFile.IsCompoundFile(content))
            return null;

        byte[]? workbook;
        try
        {
            workbook = CompoundFile.Open(content).ReadStream(WorkbookStream);
        }
        catch (InvalidDataException)
        {
            return null;
        }

        if (workbook is null)
            return null;

        var sheet = new DataSheet();
        var chart = new GraphChart();
        Walk(workbook, sheet, chart);

        // A trendline and an error bar are stored as series and are not series anybody wants
        // in a list of them ([MS-OGRAPH] 2.4.87, 2.4.86).
        List<GraphSeries> drawn = [.. chart.Series.Where(static series => !series.IsAuxiliary)];
        return drawn.Count == 0
            ? null
            : new Chart
            {
                Location = embedded.Location,
                Title = embedded.DisplayName,
                Kind = chart.Kind,
                Series = [.. drawn.Select(series => Build(series, sheet, chart.SeriesInRows, chart.Kind))],
            };
    }

    /// <summary>Walks the records of the stream, gathering the cells and the series.</summary>
    private static void Walk(byte[] stream, DataSheet sheet, GraphChart chart)
    {
        int at = 0;
        while (at + HeaderBytes <= stream.Length)
        {
            ushort type = BinaryPrimitives.ReadUInt16LittleEndian(stream.AsSpan(at));
            int length = BinaryPrimitives.ReadUInt16LittleEndian(stream.AsSpan(at + 2));
            int body = at + HeaderBytes;
            if (body + length > stream.Length)
                return;

            // A record longer than the format's maximum payload runs on into Continue records
            // ([MS-OGRAPH] 2.4.23), so the tail is joined back on before the record is read.
            int next = body + length;
            ReadOnlySpan<byte> data = Joined(stream, body, length, ref next);

            Take(type, data, sheet, chart);
            at = next;
        }
    }

    /// <summary>
    /// The whole of a record, with everything its <c>Continue</c> records carry joined on. The
    /// common case — no continuation — hands back a window on the stream and copies nothing.
    /// </summary>
    /// <param name="stream">The workbook stream.</param>
    /// <param name="body">Where the record's own payload begins.</param>
    /// <param name="length">How long that payload is.</param>
    /// <param name="next">Where the next record begins, moved past every continuation.</param>
    private static ReadOnlySpan<byte> Joined(byte[] stream, int body, int length, ref int next)
    {
        if (!Continues(stream, next))
            return stream.AsSpan(body, length);

        var joined = new List<byte>(length * 2);
        joined.AddRange(stream.AsSpan(body, length));

        while (Continues(stream, next))
        {
            int size = BinaryPrimitives.ReadUInt16LittleEndian(stream.AsSpan(next + 2));
            if (next + HeaderBytes + size > stream.Length)
                break;

            joined.AddRange(stream.AsSpan(next + HeaderBytes, size));
            next += HeaderBytes + size;
        }

        return joined.ToArray();
    }

    private static bool Continues(byte[] stream, int at) =>
        at + HeaderBytes <= stream.Length && BinaryPrimitives.ReadUInt16LittleEndian(stream.AsSpan(at)) == RecordContinue;

    /// <summary>Reads one record into whatever part of the chart it belongs to.</summary>
    private static void Take(ushort type, ReadOnlySpan<byte> data, DataSheet sheet, GraphChart chart)
    {
        switch (type)
        {
            case RecordNumber when data.Length >= 15:
                sheet.Set(Row(data), Column(data), BinaryPrimitives.ReadDoubleLittleEndian(data[7..]));
                return;
            case RecordLabel when data.Length >= 8:
                sheet.Set(Row(data), Column(data), ShortString(data[7..]));
                return;
            case RecordSeries:
                chart.Series.Add(chart.Current = new GraphSeries());
                return;
            case RecordSeriesText when chart.Current is { Name: null } && data.Length >= 3:
                chart.Current.Name = ShortString(data[2..]);
                return;
            case RecordDataReference when chart.Current is not null && data.Length >= 8:
                chart.Current.Reference(data[0], data[1], BinaryPrimitives.ReadUInt16LittleEndian(data[6..]));
                return;
            case RecordTrendline or RecordErrorBar when chart.Current is not null:
                chart.Current.IsAuxiliary = true;
                return;
            case RecordOrientation when data.Length >= 1:
                chart.SeriesInRows = data[0] != 0;
                return;
            case RecordSeriesList:
                chart.Group(data);
                return;
            case RecordBeginOfFile or RecordEndOfFile or RecordChart:
                chart.Current = null;
                return;
            default:
                if (Kind(type) is { } kind)
                    chart.Opened(kind);

                return;
        }
    }

    /// <summary>Turns one series' references into the numbers and labels it draws.</summary>
    private static ChartSeries Build(GraphSeries series, DataSheet sheet, bool inRows, ChartKind fallback)
    {
        IReadOnlyList<object?> values = series.Values is { } line ? sheet.Line(line, inRows) : [];
        IReadOnlyList<object?> categories = series.Categories is { } labels ? sheet.Line(labels, inRows) : [];
        IReadOnlyList<object?> bubbles = series.BubbleSizes is { } sizes ? sheet.Line(sizes, inRows) : [];

        return new ChartSeries
        {
            Name = series.Name ?? (series.Title is { } title ? sheet.Text(sheet.Line(title, inRows), 0) : null),
            Values = [.. values.Skip(1).Select(static cell => cell as double?)],
            BubbleSizes = [.. bubbles.Skip(1).Select(static cell => cell as double?)],
            Categories = [.. Enumerable.Range(1, Math.Max(values.Count, categories.Count) - 1)
                .Select(index => sheet.Text(categories, index) ?? index.ToString(System.Globalization.CultureInfo.InvariantCulture))],
            Kind = series.Kind ?? fallback,
        };
    }

    private static int Row(ReadOnlySpan<byte> data) => BinaryPrimitives.ReadUInt16LittleEndian(data);

    private static int Column(ReadOnlySpan<byte> data) => BinaryPrimitives.ReadUInt16LittleEndian(data[2..]);

    /// <summary>A string counted in characters, one byte or two apiece ([MS-OGRAPH] 2.5.27).</summary>
    private static string ShortString(ReadOnlySpan<byte> data)
    {
        if (data.Length < 2)
            return string.Empty;

        int count = data[0];
        bool wide = (data[1] & 0x1) != 0;
        int bytes = wide ? count * 2 : count;
        if (bytes <= 0 || 2 + bytes > data.Length)
            return string.Empty;

        ReadOnlySpan<byte> text = data.Slice(2, bytes);
        return wide ? Encoding.Unicode.GetString(text) : Encoding.Latin1.GetString(text);
    }

    private static ChartKind? Kind(ushort type) => type switch
    {
        RecordBar => ChartKind.Bar,
        RecordLine => ChartKind.Line,
        RecordPie => ChartKind.Pie,
        RecordArea => ChartKind.Area,
        RecordScatter => ChartKind.Scatter,
        RecordRadar or RecordRadarArea => ChartKind.Radar,
        RecordSurface => ChartKind.Surface,
        RecordBubble => ChartKind.Bubble,
        _ => null,
    };

    /// <summary>The chart as the stream describes it, before the cells are joined to the series.</summary>
    /// <remarks>
    /// A chart can combine kinds by putting its series into groups, each opened by a record
    /// naming what it draws and closed by a <c>SeriesList</c> saying which series are in it
    /// ([MS-OGRAPH] 2.4.90). The first group is the chart's kind — and is the one that may
    /// leave the list out, in which case every series that is not in a later group belongs to it.
    /// </remarks>
    private sealed class GraphChart
    {
        private ChartKind? _pending;
        private bool _grouped;

        public List<GraphSeries> Series { get; } = [];

        /// <summary>The series being read, or nothing between them.</summary>
        public GraphSeries? Current { get; set; }

        public ChartKind Kind { get; private set; }

        /// <summary>Whether a series is a row of the data sheet rather than a column.</summary>
        public bool SeriesInRows { get; set; } = true;

        /// <summary>A record naming what a chart group draws.</summary>
        /// <param name="kind">What it draws.</param>
        public void Opened(ChartKind kind)
        {
            if (!_grouped && Kind == ChartKind.Unknown)
                Kind = kind;

            _pending = kind;
        }

        /// <summary>
        /// The list closing a chart group, which says which series are drawn its way. The
        /// indices are one-based and name the series in the order the stream declared them.
        /// </summary>
        /// <param name="data">The record's payload.</param>
        public void Group(ReadOnlySpan<byte> data)
        {
            _grouped = true;
            if (_pending is not { } kind || data.Length < 2)
                return;

            int count = BinaryPrimitives.ReadUInt16LittleEndian(data);
            for (int i = 0; i < count && 2 + ((i + 1) * 2) <= data.Length; i++)
            {
                int index = BinaryPrimitives.ReadUInt16LittleEndian(data[(2 + (i * 2))..]) - 1;
                if (index >= 0 && index < Series.Count)
                    Series[index].Kind = kind;
            }
        }
    }

    /// <summary>
    /// One series, as the rows or columns it points at. Which part of the series a reference
    /// is for is the first byte of the record ([MS-OGRAPH] 2.4.18).
    /// </summary>
    private sealed class GraphSeries
    {
        public string? Name { get; set; }

        /// <summary>Where the name would be read from, when no cached one was stored.</summary>
        public int? Title { get; private set; }

        public int? Values { get; private set; }

        public int? Categories { get; private set; }

        /// <summary>Where the bubble sizes are, for the one kind of chart that has a third stream.</summary>
        public int? BubbleSizes { get; private set; }

        /// <summary>What this series is drawn as, when a chart group claimed it.</summary>
        public ChartKind? Kind { get; set; }

        /// <summary>
        /// Whether this is a trendline or an error bar rather than data. Both are written as
        /// series and neither is one a caller asking for the series wants back.
        /// </summary>
        public bool IsAuxiliary { get; set; }

        public void Reference(byte part, byte source, int line)
        {
            // A source of zero means the values are generated rather than taken from a cell.
            if (source == 0)
                return;

            switch (part)
            {
                case 0: Title ??= line; break;
                case 1: Values ??= line; break;
                case 2: Categories ??= line; break;
                case 3: BubbleSizes ??= line; break;
            }
        }
    }

    /// <summary>The grid of cells a Microsoft Graph object keeps its numbers in.</summary>
    private sealed class DataSheet
    {
        private readonly Dictionary<(int Row, int Column), object> _cells = [];
        private int _rows;
        private int _columns;

        public void Set(int row, int column, object value)
        {
            _cells[(row, column)] = value;
            _rows = Math.Max(_rows, row + 1);
            _columns = Math.Max(_columns, column + 1);
        }

        /// <summary>One whole row or column of the grid, in order.</summary>
        /// <param name="index">Which row or column.</param>
        /// <param name="isRow">Whether the index names a row.</param>
        public IReadOnlyList<object?> Line(int index, bool isRow)
        {
            int count = isRow ? _columns : _rows;
            var line = new object?[count];
            for (int i = 0; i < count; i++)
                line[i] = _cells.GetValueOrDefault(isRow ? (index, i) : (i, index));

            return line;
        }

        /// <summary>One cell of a line as text, or nothing when the line does not reach it.</summary>
        public string? Text(IReadOnlyList<object?> line, int index) => index < line.Count
            ? line[index] switch
            {
                string text => text,
                double number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
                _ => null,
            }
            : null;
    }
}
