using System.Globalization;

namespace Quillwright.Editing;

/// <summary>
/// Evaluates the expression of a formula field (ISO/IEC 29500-1 §17.16.3).
/// </summary>
/// <remarks>
/// <para>
/// The grammar is small — constants, the operators of §17.16.3.3, the functions of §17.16.3.4,
/// and names that stand for a value — so this is an ordinary recursive descent parser rather
/// than anything table-driven. Every arithmetic term is a real number, as the specification
/// requires: <c>1/3</c> is a third, not zero.
/// </para>
/// <para>
/// Names are resolved by the caller, because what a name means depends on where the field
/// sits: a bookmark anywhere in the document, or a cell of the table the field is in.
/// </para>
/// </remarks>
internal sealed class FieldFormula
{
    private readonly string _text;
    private readonly IFormulaNames _names;
    private int _at;

    private FieldFormula(string text, IFormulaNames names)
    {
        _text = text;
        _names = names;
    }

    /// <summary>Evaluates an expression, or returns <see langword="null"/> when it is not one.</summary>
    /// <param name="expression">The text after the <c>=</c>.</param>
    /// <param name="names">Resolves bookmarks, cell references and cell ranges.</param>
    public static double? Evaluate(string expression, IFormulaNames names)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return null;

        var parser = new FieldFormula(expression, names);
        try
        {
            double value = parser.Comparison();
            parser.SkipSpace();
            return parser._at == parser._text.Length ? value : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>The lowest precedence: the relational operators, which yield one or zero.</summary>
    private double Comparison()
    {
        double left = Additive();
        if (Operator() is not { } comparison)
            return left;

        double right = Additive();
        bool result = comparison switch
        {
            "=" => left == right,
            "<>" => left != right,
            "<=" => left <= right,
            ">=" => left >= right,
            "<" => left < right,
            _ => left > right,
        };

        return result ? 1 : 0;
    }

    private string? Operator()
    {
        SkipSpace();
        if (_at >= _text.Length)
            return null;

        if (Ahead("<>") || Ahead("<=") || Ahead(">="))
        {
            string found = _text.Substring(_at, 2);
            _at += 2;
            return found;
        }

        if (_text[_at] is not ('=' or '<' or '>'))
            return null;

        return _text[_at++].ToString();
    }

    private double Additive()
    {
        double value = Multiplicative();
        while (true)
        {
            SkipSpace();
            if (_at >= _text.Length || _text[_at] is not ('+' or '-'))
                return value;

            char op = _text[_at++];
            double right = Multiplicative();
            value = op == '+' ? value + right : value - right;
        }
    }

    private double Multiplicative()
    {
        double value = Power();
        while (true)
        {
            SkipSpace();
            if (_at >= _text.Length || _text[_at] is not ('*' or '/'))
                return value;

            char op = _text[_at++];
            double right = Power();
            if (op == '/' && right == 0)
                throw new FormatException("Division by zero.");
            value = op == '*' ? value * right : value / right;
        }
    }

    private double Power()
    {
        double value = Unary();
        while (true)
        {
            SkipSpace();
            if (_at >= _text.Length || _text[_at] != '^')
                return value;

            _at++;
            value = Math.Pow(value, Unary());
        }
    }

    private double Unary()
    {
        SkipSpace();
        if (_at < _text.Length && _text[_at] == '-')
        {
            _at++;
            return -Unary();
        }

        if (_at < _text.Length && _text[_at] == '+')
            _at++;

        return Percent();
    }

    /// <summary>A trailing <c>%</c> makes the value a percentage of itself.</summary>
    private double Percent()
    {
        double value = Primary();
        while (true)
        {
            SkipSpace();
            if (_at >= _text.Length || _text[_at] != '%')
                return value;

            _at++;
            value /= 100;
        }
    }

    private double Primary()
    {
        SkipSpace();
        if (_at >= _text.Length)
            throw new FormatException("The expression ends where a value was expected.");

        char c = _text[_at];
        if (c == '(')
        {
            _at++;
            double value = Comparison();
            Expect(')');
            return value;
        }

        if (c == '"')
            return Number(Quoted());

        if (char.IsAsciiDigit(c) || c == '.')
            return Constant();

        return Named();
    }

    /// <summary>A number as §17.16.3.1 writes one: digits, an optional point, no exponent.</summary>
    private double Constant()
    {
        int start = _at;
        while (_at < _text.Length && (char.IsAsciiDigit(_text[_at]) || _text[_at] == '.'))
            _at++;

        return double.TryParse(_text[start.._at], NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
            ? value
            : throw new FormatException($"'{_text[start.._at]}' is not a number.");
    }

    /// <summary>A function call, a constant such as <c>TRUE</c>, or a name standing for a value.</summary>
    private double Named()
    {
        string name = Identifier();
        if (name.Length == 0)
            throw new FormatException($"'{_text[_at..]}' is not something this can evaluate.");

        SkipSpace();
        if (_at < _text.Length && _text[_at] == '(')
        {
            return name.Equals("DEFINED", StringComparison.OrdinalIgnoreCase)
                ? Defined()
                : FieldFunctions.Call(name, Arguments(name));
        }

        return name.ToUpperInvariant() switch
        {
            "TRUE" => 1,
            "FALSE" => 0,
            _ => Number(_names.Resolve(name) ?? throw new FormatException($"'{name}' has no value.")),
        };
    }

    /// <summary>
    /// <c>DEFINED</c> asks whether its argument evaluates at all, so unlike every other
    /// function it must survive an argument that does not.
    /// </summary>
    private double Defined()
    {
        int mark = _at;
        try
        {
            return Arguments("DEFINED").Count == 1 ? 1 : 0;
        }
        catch (FormatException)
        {
            _at = mark;
            SkipBalanced();
            return 0;
        }
    }

    /// <summary>Steps over a parenthesised group without evaluating what is inside it.</summary>
    private void SkipBalanced()
    {
        Expect('(');
        int depth = 1;
        while (_at < _text.Length && depth > 0)
        {
            if (_text[_at] == '(')
                depth++;
            else if (_text[_at] == ')')
                depth--;
            _at++;
        }
    }

    /// <summary>
    /// The arguments of a function call. A function taking a list gets its arguments as
    /// written, because the ones that name cells expand to a whole row or column of them.
    /// </summary>
    private List<double> Arguments(string name)
    {
        Expect('(');
        var values = new List<double>();
        SkipSpace();
        if (_at < _text.Length && _text[_at] == ')')
        {
            _at++;
            return values;
        }

        while (true)
        {
            if (FieldFunctions.TakesList(name) && Range() is { } cells)
                values.AddRange(cells);
            else
                values.Add(Comparison());

            SkipSpace();
            if (_at < _text.Length && _text[_at] is ',' or ';')
            {
                _at++;
                continue;
            }

            Expect(')');
            return values;
        }
    }

    /// <summary>
    /// A range of cells, when the argument is one: <c>ABOVE</c>, <c>A1:B2</c>, or a single
    /// cell name. Anything else is an ordinary expression and is left where it was.
    /// </summary>
    private IReadOnlyList<double>? Range()
    {
        int mark = _at;
        SkipSpace();
        int start = _at;

        while (_at < _text.Length && (char.IsAsciiLetterOrDigit(_text[_at]) || _text[_at] == ':'))
            _at++;

        string reference = _text[start.._at];
        SkipSpace();
        bool complete = reference.Length > 0 && _at < _text.Length && _text[_at] is ',' or ';' or ')';
        if (complete && _names.ResolveRange(reference) is { } cells)
            return cells;

        _at = mark;
        return null;
    }

    private string Identifier()
    {
        int start = _at;
        while (_at < _text.Length && (char.IsLetterOrDigit(_text[_at]) || _text[_at] is '_' or '.'))
            _at++;

        return _text[start.._at];
    }

    private string Quoted()
    {
        _at++;
        int start = _at;
        while (_at < _text.Length && _text[_at] != '"')
            _at++;

        string value = _text[start.._at];
        Expect('"');
        return value;
    }

    /// <summary>A value that arrived as text, which the specification allows an operand to be.</summary>
    private static double Number(string text) =>
        double.TryParse(text.Trim(), NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out double value)
            ? value
            : throw new FormatException($"'{text}' is not a number.");

    private bool Ahead(string token) =>
        _at + token.Length <= _text.Length && _text.AsSpan(_at, token.Length).SequenceEqual(token);

    private void Expect(char c)
    {
        SkipSpace();
        if (_at >= _text.Length || _text[_at] != c)
            throw new FormatException($"'{c}' was expected in the expression.");
        _at++;
    }

    private void SkipSpace()
    {
        while (_at < _text.Length && char.IsWhiteSpace(_text[_at]))
            _at++;
    }
}

/// <summary>What a name in a formula stands for.</summary>
internal interface IFormulaNames
{
    /// <summary>The text a bookmark or a single cell holds, or <see langword="null"/> for an unknown name.</summary>
    /// <param name="name">The name as it was written.</param>
    string? Resolve(string name);

    /// <summary>
    /// The numbers a reference to several cells stands for, or <see langword="null"/> when
    /// the reference names no cells.
    /// </summary>
    /// <param name="reference">A direction such as <c>ABOVE</c>, or a range such as <c>A1:B3</c>.</param>
    IReadOnlyList<double>? ResolveRange(string reference);
}
