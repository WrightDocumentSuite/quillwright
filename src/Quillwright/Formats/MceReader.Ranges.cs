namespace Quillwright.Formats;

/// <summary>
/// Finds where each branch of a compatibility block keeps its content, by position in the
/// original markup.
/// </summary>
/// <remarks>
/// The tags are found in the text rather than by re-serializing the elements, for the same
/// reason the text-box scan is: a fragment serialized on its own gains the namespace
/// declarations its ancestors supplied, so the result would no longer be found in the bytes it
/// came from. The scan is only trusted when it agrees with what the parser saw.
/// </remarks>
internal static partial class MceReader
{
    private const string AlternateContentElement = "AlternateContent";

    /// <summary>Where the content of each branch sits, in the order the branches appear.</summary>
    /// <param name="markup">The whole <c>mc:AlternateContent</c> element.</param>
    public static List<Range> BranchRanges(string markup)
    {
        var ranges = new List<Range>();
        int depth = 0;
        int open = -1;

        foreach (Tag tag in Tags(markup))
        {
            // Only the outermost element's own branches are wanted; a compatibility block
            // nested inside one is content, and its branches belong to it.
            if (tag.Name == AlternateContentElement)
            {
                depth += tag.IsEnd ? -1 : tag.SelfClosing ? 0 : 1;
                continue;
            }

            if (depth != 1 || tag.Name is not ("Choice" or "Fallback"))
                continue;

            if (tag.SelfClosing)
                ranges.Add(new Range(tag.End, tag.End));
            else if (tag.IsEnd && open >= 0)
                (ranges, open) = (Added(ranges, open, tag.Start), -1);
            else if (!tag.IsEnd)
                open = tag.End;
        }

        return open >= 0 ? [] : ranges;
    }

    private static List<Range> Added(List<Range> ranges, int start, int end)
    {
        ranges.Add(new Range(start, Math.Max(start, end)));
        return ranges;
    }

    /// <summary>Walks the tags of a fragment, ignoring anything inside an attribute value.</summary>
    private static IEnumerable<Tag> Tags(string markup)
    {
        for (int at = markup.IndexOf('<'); at >= 0 && at < markup.Length; at = markup.IndexOf('<', at))
        {
            int end = EndOfTag(markup, at);
            if (end < 0)
                yield break;

            // A comment, a processing instruction or a CDATA section is not an element.
            if (at + 1 < markup.Length && markup[at + 1] is '!' or '?')
            {
                at = end;
                continue;
            }

            bool isEnd = at + 1 < markup.Length && markup[at + 1] == '/';
            int name = at + (isEnd ? 2 : 1);
            yield return new Tag(LocalName(markup, name, end), isEnd, markup[end - 2] == '/', at, end);
            at = end;
        }
    }

    /// <summary>The element name of a tag, with whatever prefix it was written with taken off.</summary>
    private static string LocalName(string markup, int from, int end)
    {
        int at = from;
        while (at < end && markup[at] is not ('>' or '/' or ' ' or '\t' or '\r' or '\n'))
            at++;

        int colon = markup.LastIndexOf(':', Math.Max(from, at - 1), at - from);
        return markup[(colon >= from ? colon + 1 : from)..at];
    }

    /// <summary>
    /// The index just past the tag that starts at <paramref name="from"/>. Attribute values are
    /// quoted, so the scan steps over any angle bracket inside one.
    /// </summary>
    private static int EndOfTag(string markup, int from)
    {
        char quote = '\0';
        for (int i = from; i < markup.Length; i++)
        {
            char c = markup[i];
            if (quote != '\0')
            {
                if (c == quote)
                    quote = '\0';
            }
            else if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c == '>')
            {
                return i + 1;
            }
        }

        return -1;
    }

    /// <summary>One tag of the markup.</summary>
    /// <param name="Name">Its element name, without a prefix.</param>
    /// <param name="IsEnd">Whether it closes an element.</param>
    /// <param name="SelfClosing">Whether it opens and closes one.</param>
    /// <param name="Start">Where the tag begins.</param>
    /// <param name="End">Where it ends, one past the closing bracket.</param>
    private readonly record struct Tag(string Name, bool IsEnd, bool SelfClosing, int Start, int End);
}
