using System.Buffers.Binary;
using System.Text;
using Quillwright.Doc.Writing;

namespace Quillwright.Doc.Tests;

/// <summary>
/// Builds a Microsoft Graph object of the kind Word embeds a legacy chart as ([MS-OGRAPH]).
/// </summary>
/// <remarks>
/// No document in the reference corpus contains one — a sweep in
/// <see cref="GraphChartCorpusTests"/> checks that and skips when it holds true — so the reader
/// is exercised against a file this writes rather than against something Word produced. It is
/// written from the record definitions of [MS-OGRAPH] directly and shares no code with the
/// reader, so the two can only agree by both matching the specification.
/// </remarks>
internal sealed class GraphWorkbook
{
    /// <summary>The record that opens a chart group drawing bars ([MS-OGRAPH] 2.4.11).</summary>
    public const ushort BarGroup = 0x1017;

    /// <summary>The record that opens a chart group drawing lines ([MS-OGRAPH] 2.4.63).</summary>
    public const ushort LineGroup = 0x1018;

    /// <summary>The record that opens a chart group drawing bubbles ([MS-OGRAPH] 2.4.85).</summary>
    public const ushort BubbleGroup = 0x104A;

    private const ushort Number = 0x0003;
    private const ushort EndOfFile = 0x000A;
    private const ushort Continue = 0x003C;
    private const ushort Label = 0x0204;
    private const ushort BeginOfFile = 0x0809;
    private const ushort Chart = 0x1002;
    private const ushort Series = 0x1003;
    private const ushort SeriesText = 0x100D;
    private const ushort SeriesList = 0x1016;
    private const ushort Begin = 0x1033;
    private const ushort End = 0x1034;
    private const ushort Trendline = 0x104B;
    private const ushort DataReference = 0x1051;
    private const ushort Orientation = 0x1055;

    private readonly List<byte> _stream = [];
    private readonly List<(int Row, int Column, object Value)> _cells = [];
    private readonly List<Line> _series = [];
    private readonly List<(ushort Record, int[] Series)> _groups = [];

    /// <summary>Whether a series is a row of the data sheet rather than a column.</summary>
    public bool SeriesInRows { get; set; } = true;

    /// <summary>
    /// How many bytes of a long record's payload go in the record itself, the rest running on
    /// into a <c>Continue</c>; zero writes every record whole, as a small chart's would be.
    /// </summary>
    public int SplitLongRecordsAt { get; set; }

    /// <summary>Puts a value in the data sheet.</summary>
    public GraphWorkbook Cell(int row, int column, object value)
    {
        _cells.Add((row, column, value));
        return this;
    }

    /// <summary>Adds a series drawing one line of the data sheet.</summary>
    /// <param name="name">The cached name, or <see langword="null"/> to leave it to the sheet.</param>
    /// <param name="values">Index of the row or column holding the values.</param>
    /// <param name="categories">Index of the row or column holding the category labels.</param>
    /// <param name="bubbles">Index of the line holding bubble sizes, for a bubble chart.</param>
    /// <param name="trendline">Whether this series is a trendline rather than data.</param>
    public GraphWorkbook AddSeries(
        string? name, int values, int categories, int? bubbles = null, bool trendline = false)
    {
        _series.Add(new Line(name, values, categories, bubbles, trendline));
        return this;
    }

    /// <summary>
    /// Adds a chart group: a record saying what it draws, then the list of the series drawn
    /// that way ([MS-OGRAPH] 2.4.90). Series are numbered from one.
    /// </summary>
    /// <param name="record">Which chart-group record opens it.</param>
    /// <param name="series">The one-based numbers of the series in the group.</param>
    public GraphWorkbook AddGroup(ushort record, params int[] series)
    {
        _groups.Add((record, series));
        return this;
    }

    /// <summary>Builds the compound file the object pool would hold.</summary>
    public byte[] Build()
    {
        Record(BeginOfFile, [.. Sixteen(0x0680), .. Sixteen(0x0005), .. Sixteen(0), .. Sixteen(0x07CD)]);
        Record(EndOfFile, []);

        Record(BeginOfFile, [.. Sixteen(0x0680), .. Sixteen(0x8000), .. Sixteen(0), .. Sixteen(0x07CD)]);
        Record(Chart, new byte[16]);
        Record(Begin, []);
        Record(Orientation, [SeriesInRows ? (byte)1 : (byte)0, 0, 0, 0, 0, 1]);

        // A chart with no groups of its own is one plain group of bars, which is what a chart
        // Word makes from a fresh data sheet is.
        if (_groups.Count == 0)
            Record(BarGroup, new byte[6]);

        foreach ((int row, int column, object value) in _cells)
            WriteCell(row, column, value);

        foreach (Line line in _series)
            WriteSeries(line);

        foreach ((ushort record, int[] series) in _groups)
            WriteGroup(record, series);

        Record(End, []);
        Record(EndOfFile, []);

        var container = new CompoundFileWriter();
        container.Add("Workbook", [.. _stream]);
        return container.Build();
    }

    private void WriteCell(int row, int column, object value)
    {
        if (value is string text)
        {
            Record(Label, [.. Sixteen(row), .. Sixteen(column), 0, .. Sixteen(0), .. ShortString(text)]);
            return;
        }

        var number = new byte[8];
        BinaryPrimitives.WriteDoubleLittleEndian(number, Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture));
        Record(Number, [.. Sixteen(row), .. Sixteen(column), 0, .. Sixteen(0), .. number]);
    }

    private void WriteSeries(Line line)
    {
        // sdtX, sdtY, cValx, cValy, sdtBSize, cValBSize.
        Record(Series, [.. Sixteen(3), .. Sixteen(1), .. Sixteen(0), .. Sixteen(0), .. Sixteen(1), .. Sixteen(0)]);
        Record(Begin, []);

        Record(DataReference, Reference(part: 0, line: line.Values));
        if (line.Name is not null)
            Record(SeriesText, [.. Sixteen(0), .. ShortString(line.Name)]);

        Record(DataReference, Reference(part: 1, line: line.Values));
        Record(DataReference, Reference(part: 2, line: line.Categories));

        // The fourth reference is always written; a chart that draws no bubbles says so by
        // naming no source rather than by leaving the record out.
        Record(DataReference, line.Bubbles is { } bubbles
            ? Reference(part: 3, line: bubbles)
            : Reference(part: 3, line: 0, source: 0));

        if (line.IsTrendline)
            Record(Trendline, new byte[28]);

        Record(End, []);
    }

    private void WriteGroup(ushort record, int[] series)
    {
        Record(record, new byte[6]);
        Record(Begin, []);

        var list = new List<byte>(Sixteen(series.Length));
        foreach (int index in series)
            list.AddRange(Sixteen(index));

        Record(SeriesList, [.. list]);
        Record(End, []);
    }

    /// <summary>A BRAI record ([MS-OGRAPH] 2.4.18): which part of the series draws which line.</summary>
    /// <param name="part">Which part of the series the data is for.</param>
    /// <param name="line">The row or column it comes from.</param>
    /// <param name="source">Where the data comes from; zero means it is not used at all.</param>
    private static byte[] Reference(byte part, int line, byte source = 1) =>
        [part, source, .. Sixteen(0x0002), .. Sixteen(0), .. Sixteen(line)];

    private static byte[] Sixteen(int value)
    {
        var bytes = new byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)value);
        return bytes;
    }

    /// <summary>A string counted in characters ([MS-OGRAPH] 2.5.27).</summary>
    private static byte[] ShortString(string text) =>
        [(byte)text.Length, 1, .. Encoding.Unicode.GetBytes(text)];

    /// <summary>
    /// Writes one record, running its payload on into a <c>Continue</c> when the fixture was
    /// asked to split long ones — which is what the format does with any record too big for the
    /// maximum payload ([MS-OGRAPH] 2.4.23).
    /// </summary>
    private void Record(ushort type, byte[] body)
    {
        int split = SplitLongRecordsAt;
        if (split <= 0 || body.Length <= split)
        {
            Emit(type, body);
            return;
        }

        Emit(type, body[..split]);
        for (int at = split; at < body.Length; at += split)
            Emit(Continue, body[at..Math.Min(at + split, body.Length)]);
    }

    private void Emit(ushort type, byte[] body)
    {
        _stream.AddRange(Sixteen(type));
        _stream.AddRange(Sixteen(body.Length));
        _stream.AddRange(body);
    }

    /// <summary>One series as the fixture was asked for it.</summary>
    /// <param name="Name">The cached name, when it has one.</param>
    /// <param name="Values">Which line of the sheet holds its numbers.</param>
    /// <param name="Categories">Which line holds its labels.</param>
    /// <param name="Bubbles">Which line holds its bubble sizes, for a bubble chart.</param>
    /// <param name="IsTrendline">Whether it is a trendline rather than data.</param>
    private readonly record struct Line(string? Name, int Values, int Categories, int? Bubbles, bool IsTrendline);
}
