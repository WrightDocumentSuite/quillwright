using System.Text;

namespace Quillwright.Pdf;

/// <summary>
/// A set of Liang hyphenation patterns for one language, parsed from the TeX form
/// (<c>\patterns{…}</c> with an optional <c>\hyphenation{…}</c> exception list) or from the
/// one-pattern-per-line form hyphenation dictionaries ship in.
/// </summary>
/// <remarks>
/// <para>
/// The library ships no patterns of its own: the standard files — TeX's <c>hyphen.tex</c>, the
/// <c>hyph-*.dic</c> dictionaries LibreOffice and Hunspell use — carry licences of their own,
/// so which file to load is the caller's decision. Hand this class the file and give it to
/// <see cref="PdfExportOptions.HyphenationPatterns"/> under the language it is for.
/// </para>
/// <para>
/// A pattern is letters with digits between them (<c>hy3ph</c>): the digit is a priority at
/// that position, an odd final priority allows a break, and a dot pins the pattern to a word
/// boundary. An exception (<c>ta-ble</c>) states a word's breaks outright and wins over the
/// patterns. The algorithm is Liang's, the one TeX and Word both descend from.
/// </para>
/// </remarks>
public sealed class HyphenationPatterns
{
    private readonly Dictionary<string, byte[]> _patterns = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int[]> _exceptions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int[]> _cache = new(StringComparer.Ordinal);
    private int _longest;

    private HyphenationPatterns()
    {
    }

    /// <summary>The fewest characters a break may leave at the start of a word.</summary>
    public int LeftMin { get; set; } = 2;

    /// <summary>The fewest characters a break may carry to the next line.</summary>
    public int RightMin { get; set; } = 3;

    /// <summary>How many patterns and exceptions were read.</summary>
    public int Count => _patterns.Count + _exceptions.Count;

    /// <summary>Reads a pattern file.</summary>
    /// <param name="path">The file, TeX or one pattern per line.</param>
    public static HyphenationPatterns Load(string path) => Parse(File.ReadAllText(path));

    /// <summary>Reads patterns from a reader.</summary>
    /// <param name="reader">The content, TeX or one pattern per line.</param>
    public static HyphenationPatterns Parse(TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>Reads patterns from a string.</summary>
    /// <param name="content">The content, TeX or one pattern per line.</param>
    public static HyphenationPatterns Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var patterns = new HyphenationPatterns();
        if (content.Contains("\\patterns", StringComparison.Ordinal))
        {
            string stripped = StripTexComments(content);
            foreach (string token in Group(stripped, "\\patterns"))
                patterns.Add(token);
            foreach (string token in Group(stripped, "\\hyphenation"))
                patterns.AddException(token);
            return patterns;
        }

        bool first = true;
        foreach (string raw in content.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] is '%' or '#')
                continue;

            // A dictionary opens with the name of its encoding, and may state its own margins.
            if (first && IsEncodingName(line))
            {
                first = false;
                continue;
            }

            first = false;
            string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && parts[0] == "LEFTHYPHENMIN" && int.TryParse(parts[1], out int left))
            {
                patterns.LeftMin = Math.Max(1, left);
                continue;
            }

            if (parts.Length == 2 && parts[0] == "RIGHTHYPHENMIN" && int.TryParse(parts[1], out int right))
            {
                patterns.RightMin = Math.Max(1, right);
                continue;
            }

            foreach (string part in parts)
                patterns.Add(part);
        }

        return patterns;
    }

    /// <summary>
    /// Where a word may break: for each entry, how many characters stay on the line. Empty when
    /// the word offers no break the margins allow.
    /// </summary>
    internal int[] Opportunities(ReadOnlySpan<char> word)
    {
        if (word.Length < LeftMin + RightMin)
            return [];

        string lower = word.ToString().ToLowerInvariant();
        if (_exceptions.TryGetValue(lower, out int[]? listed))
            return listed;

        if (_cache.TryGetValue(lower, out int[]? cached))
            return cached;

        int[] found = Compute(lower);
        if (_cache.Count > 100_000)
            _cache.Clear();

        _cache[lower] = found;
        return found;
    }

    private int[] Compute(string lower)
    {
        string dotted = "." + lower + ".";
        Span<byte> levels = dotted.Length + 1 <= 128 ? stackalloc byte[dotted.Length + 1] : new byte[dotted.Length + 1];
        levels.Clear();

        Dictionary<string, byte[]>.AlternateLookup<ReadOnlySpan<char>> lookup =
            _patterns.GetAlternateLookup<ReadOnlySpan<char>>();

        for (int start = 0; start < dotted.Length; start++)
        {
            int longest = Math.Min(_longest, dotted.Length - start);
            for (int length = 1; length <= longest; length++)
            {
                if (!lookup.TryGetValue(dotted.AsSpan(start, length), out byte[]? merged))
                    continue;

                for (int at = 0; at < merged.Length; at++)
                {
                    if (merged[at] > levels[start + at])
                        levels[start + at] = merged[at];
                }
            }
        }

        var breaks = new List<int>();
        for (int keep = LeftMin; keep <= lower.Length - RightMin; keep++)
        {
            // The level before dotted[keep + 1] is the level of the break after keep characters.
            if (levels[keep + 1] % 2 == 1)
                breaks.Add(keep);
        }

        return [.. breaks];
    }

    private void Add(string token)
    {
        string cleaned = TrimNonStandard(token);
        if (cleaned.Length == 0)
            return;

        if (cleaned.Contains('-') && !cleaned.Any(char.IsDigit))
        {
            AddException(cleaned);
            return;
        }

        var letters = new StringBuilder();
        var levels = new List<byte> { 0 };
        foreach (char c in cleaned)
        {
            if (char.IsDigit(c))
            {
                levels[^1] = (byte)(c - '0');
            }
            else if (c == '.' || char.IsLetter(c))
            {
                letters.Append(c == '.' ? '.' : char.ToLowerInvariant(c));
                levels.Add(0);
            }
            else
            {
                return;
            }
        }

        if (letters.Length == 0)
            return;

        string key = letters.ToString();
        _patterns[key] = [.. levels];
        _longest = Math.Max(_longest, key.Length);
    }

    private void AddException(string token)
    {
        string cleaned = TrimNonStandard(token);
        var word = new StringBuilder();
        var breaks = new List<int>();
        foreach (char c in cleaned)
        {
            if (c == '-')
                breaks.Add(word.Length);
            else if (char.IsLetter(c))
                word.Append(char.ToLowerInvariant(c));
            else
                return;
        }

        if (word.Length > 0)
            _exceptions[word.ToString()] = [.. breaks];
    }

    /// <summary>
    /// A dictionary pattern may carry a non-standard extension after a slash; the part before it
    /// is an ordinary pattern and the extension is beyond what this implements.
    /// </summary>
    private static string TrimNonStandard(string token)
    {
        int slash = token.IndexOf('/');
        return slash < 0 ? token : token[..slash];
    }

    private static bool IsEncodingName(string line)
    {
        foreach (string prefix in (ReadOnlySpan<string>)["UTF", "ISO", "CP", "KOI", "ASCII", "microsoft-cp"])
        {
            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string StripTexComments(string content)
    {
        var kept = new StringBuilder(content.Length);
        foreach (string line in content.Split('\n'))
        {
            int comment = line.IndexOf('%');
            kept.Append(comment < 0 ? line : line[..comment]).Append('\n');
        }

        return kept.ToString();
    }

    /// <summary>The whitespace-separated tokens inside <c>\name{…}</c>.</summary>
    private static IEnumerable<string> Group(string content, string name)
    {
        int at = content.IndexOf(name + "{", StringComparison.Ordinal);
        if (at < 0)
            yield break;

        int start = at + name.Length + 1;
        int end = content.IndexOf('}', start);
        if (end < 0)
            yield break;

        foreach (string token in content[start..end].Split(
            (char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return token;
        }
    }
}
