using System.Globalization;
using Quillwright.Model;

namespace Quillwright.Editing;

/// <summary>
/// Resolves the names a field's expression can use: a bookmark anywhere in the document
/// (§17.16.3.2), and a cell of the table the field sits in (§17.16.3.5).
/// </summary>
/// <remarks>
/// A bookmark stands for the text it covers, which is why the value of one field can be an
/// operand of another: bookmark a field's result and the name reads the result back.
/// </remarks>
internal sealed class FieldNames : IFormulaNames
{
    private readonly Field _field;

    public FieldNames(Field field) => _field = field;

    private TableCell? Cell => _field.Paragraph.Parent as TableCell;

    private Table? Table => Cell?.Row?.Table;

    /// <inheritdoc />
    public string? Resolve(string name) => CellText(name) ?? Bookmark(name);

    /// <inheritdoc />
    public IReadOnlyList<double>? ResolveRange(string reference)
    {
        if (Table is not { } table || Cell is not { } cell)
            return null;

        if (Direction(reference) is { } direction)
            return Along(table, cell, direction);

        int colon = reference.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0)
            return CellText(reference) is { } single && Numeric(single) is { } value ? [value] : null;

        return Rectangle(table, reference[..colon], reference[(colon + 1)..]);
    }

    /// <summary>The value a name stands for, or the name itself when it stands for nothing.</summary>
    /// <param name="text">The operand as it was written.</param>
    public string Expand(string text) => Resolve(text) ?? text;

    /// <summary>The text a bookmark covers, taken from the paragraph its start mark is in.</summary>
    private string? Bookmark(string name)
    {
        if (_field.Paragraph.Document is not { } document)
            return null;

        foreach (BlockContainer container in document.AllContainers)
        {
            foreach (Paragraph paragraph in container.Blocks.Paragraphs)
            {
                if (Covered(paragraph, name) is { } text)
                    return text;
            }
        }

        return null;
    }

    private static string? Covered(Paragraph paragraph, string name)
    {
        int start = -1;
        int id = -1;
        foreach ((int offset, InlineMark mark) in paragraph.Marks)
        {
            if (start < 0 && mark is BookmarkStart opening && string.Equals(opening.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                start = offset;
                id = opening.Id;
            }
            else if (start >= 0 && mark is BookmarkEnd closing && closing.Id == id)
            {
                ReadOnlySpan<char> text = paragraph.AsSpan();
                return text[start..Math.Min(offset, text.Length)].ToString();
            }
        }

        return start >= 0 ? paragraph.AsSpan()[start..].ToString() : null;
    }

    /// <summary>The text of a cell named the way §17.16.3.5 names one: a column letter and a row number.</summary>
    private string? CellText(string name)
    {
        if (Table is not { } table || Address(name) is not { } address)
            return null;

        (int column, int row) = address;
        return CellAt(table, column, row)?.GetText().Trim();
    }

    /// <summary>
    /// The cell occupying a column of the grid in a row, or <see langword="null"/> when the
    /// row does not reach that far.
    /// </summary>
    /// <remarks>
    /// §17.16.3.5 names a cell after the column it sits in, and a column is a column of the
    /// table's grid — not the cell's position in its row. The two part company as soon as a
    /// cell spans more than one grid column: after a two-column merge in row 1, the second
    /// cell of that row is <c>C1</c>, and taking it as <c>B1</c> reads the wrong number into
    /// every formula that mentions it.
    /// </remarks>
    private static TableCell? CellAt(Table table, int column, int row)
    {
        if (row < 0 || row >= table.Rows.Count || column < 0)
            return null;

        int at = 0;
        foreach (TableCell cell in table.Rows[row].Cells)
        {
            at += cell.Format.GridSpan ?? 1;
            if (column < at)
                return cell;
        }

        return null;
    }

    /// <summary>Turns <c>A1</c> into a zero-based column and row, or nothing when it is not a cell name.</summary>
    private static (int Column, int Row)? Address(string name)
    {
        int at = 0;
        int column = 0;
        while (at < name.Length && char.IsAsciiLetter(name[at]))
        {
            column = (column * 26) + (char.ToUpperInvariant(name[at]) - 'A' + 1);
            at++;
        }

        if (at == 0 || at == name.Length)
            return null;

        return int.TryParse(name[at..], NumberStyles.None, CultureInfo.InvariantCulture, out int row) && row > 0
            ? (column - 1, row - 1)
            : null;
    }

    private static (int Column, int Row)? Direction(string reference) => reference.ToUpperInvariant() switch
    {
        "ABOVE" => (0, -1),
        "BELOW" => (0, 1),
        "LEFT" => (-1, 0),
        "RIGHT" => (1, 0),
        _ => null,
    };

    /// <summary>
    /// The cells running away from this one in a direction. The run ends at the first cell
    /// that holds no number, because that is where the specification says a directional list
    /// stops; a blank cell next to the formula counts as zero rather than ending it at once.
    /// </summary>
    private static IReadOnlyList<double> Along(Table table, TableCell origin, (int Column, int Row) step)
    {
        var values = new List<double>();
        if (Position(table, origin) is not { } from)
            return values;

        (int column, int row) = from;
        TableCell? previous = origin;
        for (int i = 1; ; i++)
        {
            if (CellAt(table, column + (step.Column * i), row + (step.Row * i)) is not { } cell)
                break;

            // Stepping a column at a time walks over a spanning cell once per column it
            // covers, and a cell counted twice is a sum that is wrong twice over.
            if (ReferenceEquals(cell, previous))
                continue;

            previous = cell;
            string text = cell.GetText().Trim();
            if (Numeric(text) is { } value)
                values.Add(value);
            else if (text.Length == 0 && i == 1)
                values.Add(0);
            else
                break;
        }

        // Reading away from the formula is the order the cells were visited in, not the
        // order they sit in, so a list running up or left is turned back round.
        if (step.Column < 0 || step.Row < 0)
            values.Reverse();
        return values;
    }

    private static IReadOnlyList<double>? Rectangle(Table table, string first, string last)
    {
        (int fromColumn, int fromRow, int toColumn, int toRow) = Corners(table, first, last) ?? default;
        if (toRow < fromRow || toColumn < fromColumn)
            return null;

        var values = new List<double>();
        for (int row = fromRow; row <= toRow && row < table.Rows.Count; row++)
        {
            TableCell? previous = null;
            for (int column = fromColumn; column <= toColumn; column++)
            {
                if (CellAt(table, column, row) is not { } cell || ReferenceEquals(cell, previous))
                    continue;

                previous = cell;
                if (Numeric(cell.GetText().Trim()) is { } value)
                    values.Add(value);
            }
        }

        return values;
    }

    /// <summary>
    /// The corners of a range. A range may name whole rows (<c>1:2</c>) or whole columns
    /// (<c>B:B</c>) as well as two cells, which is what the wide bounds stand for.
    /// </summary>
    private static (int FromColumn, int FromRow, int ToColumn, int ToRow)? Corners(Table table, string first, string last)
    {
        int columns = Math.Max(table.ColumnCount, 1);
        if (Address(first) is { } from && Address(last) is { } to)
            return (Math.Min(from.Column, to.Column), Math.Min(from.Row, to.Row), Math.Max(from.Column, to.Column), Math.Max(from.Row, to.Row));

        if (Column(first) is { } fromColumn && Column(last) is { } toColumn)
            return (Math.Min(fromColumn, toColumn), 0, Math.Max(fromColumn, toColumn), table.Rows.Count - 1);

        if (Row(first) is { } fromRow && Row(last) is { } toRow)
            return (0, Math.Min(fromRow, toRow), columns - 1, Math.Max(fromRow, toRow));

        return null;
    }

    private static int? Column(string name)
    {
        int column = 0;
        foreach (char c in name)
        {
            if (!char.IsAsciiLetter(c))
                return null;
            column = (column * 26) + (char.ToUpperInvariant(c) - 'A' + 1);
        }

        return name.Length > 0 ? column - 1 : null;
    }

    private static int? Row(string name) =>
        int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out int row) && row > 0 ? row - 1 : null;

    /// <summary>Where a cell sits, as a column of the grid rather than a place in its row.</summary>
    /// <seealso cref="CellAt"/>
    private static (int Column, int Row)? Position(Table table, TableCell cell)
    {
        for (int row = 0; row < table.Rows.Count; row++)
        {
            int column = 0;
            foreach (TableCell candidate in table.Rows[row].Cells)
            {
                if (ReferenceEquals(candidate, cell))
                    return (column, row);
                column += candidate.Format.GridSpan ?? 1;
            }
        }

        return null;
    }

    private static double? Numeric(string text) =>
        double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double value)
            ? value
            : null;
}
