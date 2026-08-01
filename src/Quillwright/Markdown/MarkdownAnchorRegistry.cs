using System.Text;
using Quillwright.Model;

namespace Quillwright.Markdown;

/// <summary>Assigns safe, unique HTML ids to Word bookmarks and resolves internal links.</summary>
internal sealed class MarkdownAnchorRegistry
{
    private readonly Dictionary<BookmarkStart, string> _byBookmark = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, string> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _used = new(StringComparer.OrdinalIgnoreCase);

    public string Register(BookmarkStart bookmark)
    {
        if (_byBookmark.TryGetValue(bookmark, out string? existing))
            return existing;

        string basis = Slug(bookmark.Name);
        string id = basis;
        for (int suffix = 2; !_used.Add(id); suffix++)
            id = $"{basis}-{suffix}";

        _byBookmark.Add(bookmark, id);
        if (!string.IsNullOrWhiteSpace(bookmark.Name))
            _byName.TryAdd(bookmark.Name, id);
        return id;
    }

    public string? For(BookmarkStart bookmark) =>
        _byBookmark.TryGetValue(bookmark, out string? id) ? id : null;

    public string? Resolve(string? name) =>
        name is not null && _byName.TryGetValue(name, out string? id) ? id : null;

    private static string Slug(string? name)
    {
        var builder = new StringBuilder("bookmark");
        bool hyphen = false;

        foreach (char c in name ?? string.Empty)
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
                hyphen = false;
            }
            else if (!hyphen)
            {
                builder.Append('-');
                hyphen = true;
            }
        }

        while (builder.Length > "bookmark".Length && builder[^1] == '-')
            builder.Length--;
        return builder.ToString();
    }
}
