using System.Text;

namespace Quillwright.Model;

/// <summary>
/// One switch of a field instruction (ISO/IEC 29500-1 §17.16.1).
/// </summary>
/// <param name="Name">
/// The switch without its backslash, with letters lower-cased because the specification says
/// they are compared that way: <c>*</c>, <c>#</c>, <c>@</c>, <c>h</c>, <c>ca</c>.
/// </param>
/// <param name="Argument">What follows the switch, unquoted, or <see langword="null"/> for a switch that takes nothing.</param>
public readonly record struct FieldSwitch(string Name, string? Argument)
{
    /// <inheritdoc />
    public override string ToString() => Argument is null ? "\\" + Name : $"\\{Name} {Argument}";
}

/// <summary>
/// A field instruction taken apart: the field's name, its positional arguments and its
/// switches (ISO/IEC 29500-1 §17.16.1).
/// </summary>
/// <remarks>
/// <para>
/// The instruction is one string in the file — <c>DATE \@ "dd.MM.yyyy" \h</c> — and the
/// quoting rules are its own: double quotes group an argument that contains spaces, a
/// backslash escapes a quote or another backslash inside them, and a backslash outside them
/// begins a switch with no space allowed after it.
/// </para>
/// <para>
/// Whether a switch takes an argument is decided per field by §17.16.5, which this does not
/// model. The token after a switch is taken as its argument unless that token is itself a
/// switch, which is what every field in the specification actually does.
/// </para>
/// </remarks>
public sealed class FieldInstruction
{
    private FieldInstruction(string raw, string name, IReadOnlyList<string> arguments, IReadOnlyList<FieldSwitch> switches)
    {
        Raw = raw;
        Name = name;
        Arguments = arguments;
        Switches = switches;
    }

    /// <summary>The instruction as it was written.</summary>
    public string Raw { get; }

    /// <summary>The field's name, upper-cased, or <c>=</c> for a formula field.</summary>
    public string Name { get; }

    /// <summary>The positional arguments, in order and with their quotes removed.</summary>
    public IReadOnlyList<string> Arguments { get; }

    /// <summary>The switches, in the order they were written.</summary>
    public IReadOnlyList<FieldSwitch> Switches { get; }

    /// <summary>Whether this is a formula field, whose single argument is the expression.</summary>
    public bool IsFormula => Name == "=";

    /// <summary>The general formatting switch (<c>\*</c>), such as <c>Upper</c> or <c>ROMAN</c>.</summary>
    public string? GeneralFormat => Argument("*");

    /// <summary>The numeric picture (<c>\#</c>), such as <c>#,##0.00</c>.</summary>
    public string? NumericPicture => Argument("#");

    /// <summary>The date picture (<c>\@</c>), such as <c>dddd, MMMM d, yyyy</c>.</summary>
    public string? DatePicture => Argument("@");

    /// <summary>Whether the instruction carries a switch.</summary>
    /// <param name="name">The switch without its backslash; letters are matched case-insensitively.</param>
    public bool Has(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        string wanted = name.ToLowerInvariant();
        foreach (FieldSwitch entry in Switches)
        {
            if (entry.Name == wanted)
                return true;
        }

        return false;
    }

    /// <summary>The argument of a switch, or <see langword="null"/> when it is absent or takes none.</summary>
    /// <param name="name">The switch without its backslash; letters are matched case-insensitively.</param>
    public string? Argument(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        string wanted = name.ToLowerInvariant();
        foreach (FieldSwitch entry in Switches)
        {
            if (entry.Name == wanted)
                return entry.Argument;
        }

        return null;
    }

    /// <summary>Takes an instruction apart.</summary>
    /// <param name="instruction">The instruction, for example <c>PAGE \* MERGEFORMAT</c>.</param>
    public static FieldInstruction Parse(string instruction)
    {
        ArgumentNullException.ThrowIfNull(instruction);
        string raw = instruction.Trim();

        // A formula is the one instruction whose argument is not a token: it is an
        // expression, spaces and all, running up to the first switch.
        if (raw.StartsWith('='))
        {
            int at = FirstSwitch(raw, 1);
            string expression = (at < 0 ? raw[1..] : raw[1..at]).Trim();
            Split(Tokenize(at < 0 ? string.Empty : raw[at..]), 0, out _, out IReadOnlyList<FieldSwitch> formulaSwitches);
            return new FieldInstruction(raw, "=", expression.Length > 0 ? [expression] : [], formulaSwitches);
        }

        List<Token> tokens = Tokenize(raw);
        string name = tokens.Count > 0 && !tokens[0].IsSwitch ? tokens[0].Text.ToUpperInvariant() : string.Empty;
        Split(tokens, name.Length > 0 ? 1 : 0, out IReadOnlyList<string> arguments, out IReadOnlyList<FieldSwitch> switches);
        return new FieldInstruction(raw, name, arguments, switches);
    }

    /// <inheritdoc />
    public override string ToString() => Raw;

    /// <summary>Sorts the tokens after the name into positional arguments and switches.</summary>
    private static void Split(
        List<Token> tokens, int from, out IReadOnlyList<string> arguments, out IReadOnlyList<FieldSwitch> switches)
    {
        var positional = new List<string>();
        var found = new List<FieldSwitch>();

        for (int i = from; i < tokens.Count; i++)
        {
            if (!tokens[i].IsSwitch)
            {
                positional.Add(tokens[i].Text);
                continue;
            }

            string name = tokens[i].Text;
            string? argument = i + 1 < tokens.Count && !tokens[i + 1].IsSwitch ? tokens[++i].Text : null;
            found.Add(new FieldSwitch(name, argument));
        }

        arguments = positional;
        switches = found;
    }

    /// <summary>Where the first switch of an instruction begins, ignoring quoted text.</summary>
    private static int FirstSwitch(string text, int from)
    {
        bool quoted = false;
        for (int i = from; i < text.Length; i++)
        {
            if (text[i] == '"')
                quoted = !quoted;
            else if (text[i] == '\\' && !quoted)
                return i;
        }

        return -1;
    }

    private static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        var builder = new StringBuilder();
        bool quoted = false;
        bool started = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '\\' && i + 1 < text.Length && text[i + 1] is '"' or '\\')
                    builder.Append(text[++i]);
                else if (c == '"')
                    quoted = false;
                else
                    builder.Append(c);
                continue;
            }

            if (c == '"')
            {
                quoted = true;
                started = true;
            }
            else if (c == '\\')
            {
                Flush();
                i = ReadSwitch(text, i, tokens);
            }
            else if (char.IsWhiteSpace(c))
            {
                Flush();
            }
            else
            {
                started = true;
                builder.Append(c);
            }
        }

        Flush();
        return tokens;

        void Flush()
        {
            if (!started)
                return;

            tokens.Add(new Token(builder.ToString(), IsSwitch: false));
            builder.Clear();
            started = false;
        }
    }

    /// <summary>
    /// Reads the switch beginning at a backslash and returns the index of its last character.
    /// A switch is one symbol, or one or two letters; anything after that is its argument.
    /// </summary>
    private static int ReadSwitch(string text, int backslash, List<Token> tokens)
    {
        int start = backslash + 1;
        if (start >= text.Length || char.IsWhiteSpace(text[start]))
            return backslash;

        int end = start + 1;
        if (char.IsLetter(text[start]))
        {
            while (end < text.Length && end - start < 2 && char.IsLetter(text[end]))
                end++;
        }

        tokens.Add(new Token(text[start..end].ToLowerInvariant(), IsSwitch: true));
        return end - 1;
    }

    private readonly record struct Token(string Text, bool IsSwitch);
}
