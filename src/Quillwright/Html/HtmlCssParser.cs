using System.Globalization;
using System.Text;

namespace Quillwright.Html;

/// <summary>A declaration from an inline CSS declaration list.</summary>
internal readonly record struct HtmlCssDeclaration(string Name, string Value, bool Important);

/// <summary>
/// The small CSS syntax layer needed by the HTML importer. It does not implement a CSS
/// cascade or selectors; it preserves the declaration and string boundaries required before
/// the importer's supported properties can be interpreted.
/// </summary>
internal static class HtmlCssParser
{
    /// <summary>Parses an element's <c>style</c> declaration list in cascade order.</summary>
    public static IEnumerable<HtmlCssDeclaration> ParseDeclarations(string style)
    {
        var normal = new List<HtmlCssDeclaration>();
        var important = new List<HtmlCssDeclaration>();

        foreach (string source in SplitDeclarations(style))
        {
            if (!TryDeclaration(source, out HtmlCssDeclaration declaration))
                continue;

            (declaration.Important ? important : normal).Add(declaration);
        }

        // Within one origin important declarations outrank every normal declaration. Source
        // order remains intact inside each group, so the last declaration still wins ties.
        foreach (HtmlCssDeclaration declaration in normal)
            yield return declaration;
        foreach (HtmlCssDeclaration declaration in important)
            yield return declaration;
    }

    /// <summary>Returns the first family in a CSS <c>font-family</c> value.</summary>
    public static string? FirstFontFamily(string value)
    {
        ReadOnlySpan<char> source = TrimCssWhitespace(value.AsSpan());
        if (source.IsEmpty)
            return null;

        string? first = null;
        int index = 0;
        bool firstFamily = true;
        while (index < source.Length)
        {
            while (index < source.Length && IsCssWhitespace(source[index]))
                index++;
            if (index >= source.Length)
                return null;

            bool quoted = source[index] is '\'' or '"';
            string? family = quoted
                ? ReadString(source, ref index)
                : ReadIdentifierSequence(source, ref index);
            if (family is not { Length: > 0 })
                return null;

            bool inherit = !quoted && AsciiEquals(family, "inherit");
            if (firstFamily)
            {
                first = inherit ? null : family;
                firstFamily = false;
            }

            while (index < source.Length && IsCssWhitespace(source[index]))
                index++;
            if (index == source.Length)
                return inherit ? null : first;

            // `inherit` is a declaration-wide keyword, never one entry in a fallback list.
            if (inherit || source[index] != ',')
                return null;

            index++;
        }

        return null;
    }

    /// <summary>Decodes one CSS identifier, or returns null when other grammar is present.</summary>
    public static string? Identifier(string value)
    {
        ReadOnlySpan<char> source = TrimCssWhitespace(value.AsSpan());
        int index = 0;
        var decoded = new StringBuilder(source.Length);
        if (!ReadIdentifier(source, ref index, decoded))
            return null;

        while (index < source.Length && IsCssWhitespace(source[index]))
            index++;
        return index == source.Length ? decoded.ToString() : null;
    }

    private static string? ReadString(ReadOnlySpan<char> source, ref int index)
    {
        char quote = source[index++];
        var decoded = new StringBuilder(source.Length - index);
        while (index < source.Length)
        {
            char character = source[index];
            if (character == quote)
            {
                index++;
                return decoded.ToString();
            }

            if (character == '\\')
            {
                AppendEscape(source, ref index, decoded);
                index = Math.Min(index + 1, source.Length);
                continue;
            }

            if (IsNewline(character))
                return null;

            decoded.Append(character);
            index++;
        }

        // CSS2 treats EOF as closing an otherwise valid string token.
        return decoded.ToString();
    }

    private static string? ReadIdentifierSequence(ReadOnlySpan<char> source, ref int index)
    {
        var family = new StringBuilder(source.Length - index);
        bool any = false;
        while (index < source.Length)
        {
            if (any)
            {
                int whitespaceStart = index;
                while (index < source.Length && IsCssWhitespace(source[index]))
                    index++;

                if (index == source.Length || source[index] == ',')
                    break;
                if (index == whitespaceStart)
                    return null;

                family.Append(' ');
            }

            if (!ReadIdentifier(source, ref index, family))
                return null;
            any = true;

            if (index < source.Length && !IsCssWhitespace(source[index]) && source[index] != ',')
                return null;
            if (index < source.Length && source[index] == ',')
                break;
        }

        return any ? family.ToString() : null;
    }

    private static bool ReadIdentifier(ReadOnlySpan<char> source, ref int index, StringBuilder decoded)
    {
        int start = index;
        if (index < source.Length && source[index] == '-')
        {
            decoded.Append('-');
            index++;
        }

        if (!ReadNameCharacter(source, ref index, decoded, startCharacter: true))
        {
            index = start;
            return false;
        }

        while (ReadNameCharacter(source, ref index, decoded, startCharacter: false))
        {
        }

        return true;
    }

    private static bool ReadNameCharacter(
        ReadOnlySpan<char> source,
        ref int index,
        StringBuilder decoded,
        bool startCharacter)
    {
        if (index >= source.Length)
            return false;

        char character = source[index];
        bool plain = character is '_' or >= '\u0080' || char.IsAsciiLetter(character) ||
                     (!startCharacter && (char.IsAsciiDigit(character) || character == '-'));
        if (plain)
        {
            decoded.Append(character);
            index++;
            return true;
        }

        if (character != '\\' || index + 1 >= source.Length || IsNewline(source[index + 1]))
            return false;

        AppendEscape(source, ref index, decoded);
        index++;
        return true;
    }

    /// <summary>ASCII-lowercases syntax without changing non-ASCII text.</summary>
    public static string AsciiLower(string value)
    {
        char[]? lowered = null;
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (character is not (>= 'A' and <= 'Z'))
                continue;

            lowered ??= value.ToCharArray();
            lowered[index] = (char)(character + ('a' - 'A'));
        }

        return lowered is null ? value : new string(lowered);
    }

    private static IEnumerable<string> SplitDeclarations(string style)
    {
        var declaration = new StringBuilder();
        char quote = '\0';
        int parentheses = 0;
        int brackets = 0;
        int braces = 0;
        bool invalid = false;

        for (int index = 0; index < style.Length; index++)
        {
            char character = style[index];
            if (quote != '\0')
            {
                if (character == '\\')
                {
                    declaration.Append(character);
                    if (++index < style.Length)
                    {
                        declaration.Append(style[index]);
                        if (style[index] == '\r' && index + 1 < style.Length && style[index + 1] == '\n')
                            declaration.Append(style[++index]);
                    }

                    continue;
                }

                if (character == quote)
                {
                    quote = '\0';
                    declaration.Append(character);
                    continue;
                }

                if (IsNewline(character))
                {
                    // Recovery continues through the next declaration delimiter, but the
                    // whole construct containing the bad string is discarded.
                    quote = '\0';
                    invalid = true;
                }

                declaration.Append(character);
                continue;
            }

            if (character == '/' && index + 1 < style.Length && style[index + 1] == '*')
            {
                int end = style.IndexOf("*/", index + 2, StringComparison.Ordinal);
                declaration.Append(' ');
                if (end < 0)
                    break;
                index = end + 1;
                continue;
            }

            if (character is '\'' or '"')
            {
                quote = character;
                declaration.Append(character);
                continue;
            }

            if (character == '\\')
            {
                declaration.Append(character);
                if (++index < style.Length)
                    declaration.Append(style[index]);
                continue;
            }

            switch (character)
            {
                case '(':
                    parentheses++;
                    break;
                case ')':
                    parentheses = Math.Max(0, parentheses - 1);
                    break;
                case '[':
                    brackets++;
                    break;
                case ']':
                    brackets = Math.Max(0, brackets - 1);
                    break;
                case '{':
                    braces++;
                    break;
                case '}':
                    braces = Math.Max(0, braces - 1);
                    break;
                case ';' when parentheses == 0 && brackets == 0 && braces == 0:
                    if (!invalid)
                        yield return declaration.ToString();
                    declaration.Clear();
                    invalid = false;
                    continue;
            }

            declaration.Append(character);
        }

        // An unexpected EOF closes open strings and grouping constructs.
        if (!invalid && declaration.Length > 0)
            yield return declaration.ToString();
    }

    private static bool TryDeclaration(string source, out HtmlCssDeclaration declaration)
    {
        declaration = default;
        int colon = TopLevelIndexOf(source.AsSpan(), ':');
        if (colon <= 0)
            return false;

        string? decodedName = Identifier(source[..colon]);
        string value = TrimCssWhitespace(source.AsSpan(colon + 1)).ToString();
        if (decodedName is not { Length: > 0 } || value.Length == 0)
            return false;

        string name = AsciiLower(decodedName);

        bool important = false;
        int bang = LastTopLevelIndexOf(value.AsSpan(), '!');
        ReadOnlySpan<char> priority = bang >= 0
            ? TrimCssWhitespace(value.AsSpan(bang + 1))
            : ReadOnlySpan<char>.Empty;
        if (bang >= 0 && Identifier(priority.ToString()) is { } priorityIdentifier &&
            AsciiEquals(priorityIdentifier, "important"))
        {
            value = TrimCssWhitespace(value.AsSpan(0, bang)).ToString();
            important = true;
        }

        if (value.Length == 0)
            return false;

        declaration = new HtmlCssDeclaration(name, value, important);
        return true;
    }

    private static int TopLevelIndexOf(ReadOnlySpan<char> value, char wanted)
    {
        char quote = '\0';
        int depth = 0;
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (quote != '\0')
            {
                if (character == '\\')
                    index++;
                else if (character == quote)
                    quote = '\0';
                continue;
            }

            if (character is '\'' or '"')
                quote = character;
            else if (character == '\\')
                index++;
            else if (character is '(' or '[' or '{')
                depth++;
            else if (character is ')' or ']' or '}')
                depth = Math.Max(0, depth - 1);
            else if (character == wanted && depth == 0)
                return index;
        }

        return -1;
    }

    private static int LastTopLevelIndexOf(ReadOnlySpan<char> value, char wanted)
    {
        int found = -1;
        char quote = '\0';
        int depth = 0;
        for (int index = 0; index < value.Length; index++)
        {
            char character = value[index];
            if (quote != '\0')
            {
                if (character == '\\')
                    index++;
                else if (character == quote)
                    quote = '\0';
                continue;
            }

            if (character is '\'' or '"')
                quote = character;
            else if (character == '\\')
                index++;
            else if (character is '(' or '[' or '{')
                depth++;
            else if (character is ')' or ']' or '}')
                depth = Math.Max(0, depth - 1);
            else if (character == wanted && depth == 0)
                found = index;
        }

        return found;
    }

    private static void AppendEscape(ReadOnlySpan<char> source, ref int index, StringBuilder target)
    {
        int next = index + 1;
        if (next >= source.Length)
        {
            index = next;
            return;
        }

        char escaped = source[next];
        if (IsNewline(escaped))
        {
            if (escaped == '\r' && next + 1 < source.Length && source[next + 1] == '\n')
                next++;
            index = next;
            return;
        }

        if (!IsHex(escaped))
        {
            target.Append(escaped);
            index = next;
            return;
        }

        int start = next;
        int length = 0;
        while (next < source.Length && length < 6 && IsHex(source[next]))
        {
            next++;
            length++;
        }

        _ = int.TryParse(source.Slice(start, length), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int scalar);
        if (scalar == 0 || scalar > 0x10FFFF || scalar is >= 0xD800 and <= 0xDFFF)
            scalar = 0xFFFD;
        target.Append(char.ConvertFromUtf32(scalar));

        if (next < source.Length && IsCssWhitespace(source[next]))
        {
            if (source[next] == '\r' && next + 1 < source.Length && source[next + 1] == '\n')
                next++;
            next++;
        }

        index = next - 1;
    }

    private static bool AsciiEquals(ReadOnlySpan<char> left, ReadOnlySpan<char> right)
    {
        if (left.Length != right.Length)
            return false;
        for (int index = 0; index < left.Length; index++)
        {
            char a = left[index];
            char b = right[index];
            if (a is >= 'A' and <= 'Z')
                a = (char)(a + ('a' - 'A'));
            if (b is >= 'A' and <= 'Z')
                b = (char)(b + ('a' - 'A'));
            if (a != b)
                return false;
        }

        return true;
    }

    private static bool IsCssWhitespace(char character) => character is ' ' or '\t' or '\n' or '\r' or '\f';

    private static ReadOnlySpan<char> TrimCssWhitespace(ReadOnlySpan<char> value)
    {
        int start = 0;
        while (start < value.Length && IsCssWhitespace(value[start]))
            start++;

        int end = value.Length;
        while (end > start && IsCssWhitespace(value[end - 1]))
            end--;

        return value[start..end];
    }

    private static bool IsNewline(char character) => character is '\n' or '\r' or '\f';

    private static bool IsHex(char character) =>
        character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F';
}
