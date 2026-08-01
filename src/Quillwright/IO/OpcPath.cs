using System.Globalization;
using System.Text;

namespace Quillwright.IO;

/// <summary>
/// Path arithmetic for OPC part names. Part names are absolute, use forward slashes
/// and are compared case-insensitively.
/// </summary>
internal static class OpcPath
{
    /// <summary>Converts a part name to the zip entry name (strips the leading slash).</summary>
    public static string ToEntryName(string partPath) => partPath.TrimStart('/');

    /// <summary>Converts a zip entry name to an absolute part name.</summary>
    public static string ToPartPath(string entryName) => entryName.StartsWith('/') ? entryName : "/" + entryName;

    /// <summary>Returns the relationships part for a given part, e.g. <c>/word/document.xml</c> to <c>/word/_rels/document.xml.rels</c>. The package root is <c>"/"</c>.</summary>
    public static string GetRelsPath(string partPath)
    {
        if (partPath == "/")
            return "/_rels/.rels";

        int slash = partPath.LastIndexOf('/');
        return $"{partPath[..slash]}/_rels/{partPath[(slash + 1)..]}.rels";
    }

    /// <summary>Returns <see langword="true"/> for a relationships part such as <c>/word/_rels/document.xml.rels</c>.</summary>
    public static bool IsRelsPath(string partPath) =>
        partPath.EndsWith(".rels", StringComparison.OrdinalIgnoreCase) &&
        partPath.Contains("/_rels/", StringComparison.OrdinalIgnoreCase);

    /// <summary>Inverse of <see cref="GetRelsPath"/>: the part a relationships part describes.</summary>
    public static string GetSourcePart(string relsPath)
    {
        int marker = relsPath.LastIndexOf("/_rels/", StringComparison.OrdinalIgnoreCase);
        string directory = relsPath[..marker];
        string name = relsPath[(marker + "/_rels/".Length)..^".rels".Length];
        return name.Length == 0 ? "/" : $"{directory}/{name}";
    }

    /// <summary>Returns the directory of a part, with a trailing slash.</summary>
    public static string GetDirectory(string partPath)
    {
        int slash = partPath.LastIndexOf('/');
        return slash <= 0 ? "/" : partPath[..(slash + 1)];
    }

    /// <summary>Resolves a relationship target relative to its source part.</summary>
    /// <param name="sourcePartPath">The part the relationship is declared on.</param>
    /// <param name="target">The <c>Target</c> attribute, which is a URI reference.</param>
    /// <remarks>
    /// A <c>Target</c> is a URI, not a part name: ECMA-376 part 2 obtains the part name by
    /// resolving it and then unescaping it, so a part called <c>/word/media/image 1.png</c>
    /// is written <c>media/image%201.png</c> and would otherwise be looked up under a name
    /// with a literal percent sign in it, and never found.
    /// </remarks>
    public static string Resolve(string sourcePartPath, string target)
    {
        string resolved = target.StartsWith('/')
            ? Normalize(target)
            : Normalize(GetDirectory(sourcePartPath) + target);

        return Unescape(resolved);
    }

    /// <summary>
    /// Expresses <paramref name="targetPartPath"/> relative to the part that references it,
    /// which is the form Word writes and the only one every consumer accepts.
    /// </summary>
    /// <param name="sourcePartPath">The part the relationship will be declared on.</param>
    /// <param name="targetPartPath">The part it points at.</param>
    /// <remarks>
    /// The result is a URI reference, so each segment is escaped on the way out — the inverse
    /// of what <see cref="Resolve"/> does on the way in. A name of nothing but unreserved
    /// characters, which is what every part Word writes is called, comes back unchanged.
    /// </remarks>
    public static string MakeRelative(string sourcePartPath, string targetPartPath)
    {
        string[] source = Segments(sourcePartPath);
        string[] target = Segments(targetPartPath);

        int shared = 0;
        // The last segment of the source is the file name, never part of the shared prefix.
        int sourceDirectoryLength = source.Length - 1;
        while (shared < sourceDirectoryLength && shared < target.Length - 1 &&
               string.Equals(source[shared], target[shared], StringComparison.OrdinalIgnoreCase))
        {
            shared++;
        }

        var builder = new StringBuilder();
        for (int i = shared; i < sourceDirectoryLength; i++)
            builder.Append("../");
        for (int i = shared; i < target.Length; i++)
        {
            builder.Append(Uri.EscapeDataString(target[i]));
            if (i < target.Length - 1)
                builder.Append('/');
        }

        return builder.ToString();

        static string[] Segments(string path) => path.TrimStart('/').Split('/');
    }

    /// <summary>
    /// Turns the escaped form of a part name back into the name itself. A name that was never
    /// escaped comes back exactly as it was, as does a stray percent sign that introduces no
    /// valid escape.
    /// </summary>
    public static string Unescape(string path) =>
        path.Contains('%', StringComparison.Ordinal) ? Uri.UnescapeDataString(path) : path;

    /// <summary>
    /// The ZIP item name a part name maps to: ECMA-376 part 2 §7.3.4 drops the leading slash
    /// and percent-encodes every non-ASCII character.
    /// </summary>
    /// <param name="partPath">The part name.</param>
    /// <remarks>
    /// Producers disagree about this. A ZIP entry can carry a name as raw UTF-8 — which is
    /// what most of them write and what <see cref="ToEntryName"/> assumes — or in the escaped
    /// form the standard asks for, so a part is looked for under both.
    /// </remarks>
    public static string ToEscapedEntryName(string partPath)
    {
        string entry = ToEntryName(partPath);
        if (entry.All(char.IsAscii))
            return entry;

        var builder = new StringBuilder(entry.Length + 8);
        foreach (char c in entry)
        {
            if (char.IsAscii(c))
            {
                builder.Append(c);
                continue;
            }

            foreach (byte b in Encoding.UTF8.GetBytes([c]))
                builder.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    /// <summary>Collapses <c>.</c> and <c>..</c> segments.</summary>
    private static string Normalize(string path)
    {
        if (!path.Contains("./", StringComparison.Ordinal))
            return path;

        var segments = new List<string>();
        foreach (Range segmentRange in path.AsSpan().Split('/'))
        {
            ReadOnlySpan<char> segment = path.AsSpan()[segmentRange];
            if (segment.IsEmpty || segment is ".")
                continue;
            if (segment is "..")
            {
                if (segments.Count > 0)
                    segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment.ToString());
        }

        return "/" + string.Join('/', segments);
    }
}
