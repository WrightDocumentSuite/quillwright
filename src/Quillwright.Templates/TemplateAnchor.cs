using System.Text.RegularExpressions;
using Quillwright.Model;

namespace Quillwright.Templates;

/// <summary>Where in a template a value is meant to go.</summary>
public enum AnchorKind
{
    /// <summary>A <c>{{Name}}</c> placeholder in the text.</summary>
    Placeholder,

    /// <summary>A content control whose tag names the value.</summary>
    ContentControl,

    /// <summary>A classic <c>MERGEFIELD</c> field.</summary>
    MergeField,
}

/// <summary>One place in a template that a value fills.</summary>
/// <param name="Kind">How the template marks the spot.</param>
/// <param name="Name">The value name.</param>
/// <param name="Paragraph">The paragraph the anchor is in.</param>
/// <param name="Start">Offset of the first character the value replaces.</param>
/// <param name="Length">Number of characters the value replaces.</param>
public readonly record struct TemplateAnchor(AnchorKind Kind, string Name, Paragraph Paragraph, int Start, int Length);

/// <summary>
/// Finds the places a template marks for filling.
/// </summary>
/// <remarks>
/// Three conventions are supported because documents in the wild use all three: braces typed
/// straight into the text, content controls bound by tag, and MERGEFIELDs left over from a
/// mail merge. They are found the same way and filled the same way, so a template can mix
/// them.
/// </remarks>
public static partial class TemplateAnchors
{
    /// <summary>Prefix on a content control tag that marks a repeated region.</summary>
    public const string RowsTagPrefix = "rows:";

    /// <summary>Prefix on a content control tag that marks a conditional region.</summary>
    public const string ConditionTagPrefix = "if:";

    /// <summary>Every anchor in a paragraph, in order of position.</summary>
    /// <param name="paragraph">Paragraph to scan.</param>
    public static IEnumerable<TemplateAnchor> Find(Paragraph paragraph)
    {
        ArgumentNullException.ThrowIfNull(paragraph);

        var found = new List<TemplateAnchor>();
        foreach (Match match in PlaceholderPattern().Matches(paragraph.Text).Cast<Match>())
            found.Add(new TemplateAnchor(AnchorKind.Placeholder, match.Groups[1].Value.Trim(), paragraph, match.Index, match.Length));

        foreach ((int start, int length, InlineRange range) in paragraph.Ranges)
        {
            if (range is InlineContentControl { Tag: { } tag } && !IsStructuralTag(tag))
                found.Add(new TemplateAnchor(AnchorKind.ContentControl, tag, paragraph, start, length));
        }

        foreach (Field field in paragraph.Fields())
        {
            if (field.Name == "MERGEFIELD" && MergeFieldName(field.Instruction) is { } name)
                found.Add(new TemplateAnchor(AnchorKind.MergeField, name, paragraph, field.ResultStart, field.ResultLength));
        }

        found.Sort(static (left, right) => left.Start.CompareTo(right.Start));
        return found;
    }

    /// <summary>Returns <see langword="true"/> when a tag marks structure rather than a value.</summary>
    /// <param name="tag">The content control tag.</param>
    public static bool IsStructuralTag(string tag) =>
        tag.StartsWith(RowsTagPrefix, StringComparison.OrdinalIgnoreCase) ||
        tag.StartsWith(ConditionTagPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>The collection or condition a structural tag names.</summary>
    /// <param name="tag">The content control tag.</param>
    public static string StructuralName(string tag) => tag[(tag.IndexOf(':') + 1)..].Trim();

    /// <summary>
    /// The part of a dotted placeholder before the dot, which names the collection a
    /// repeated region belongs to.
    /// </summary>
    /// <param name="name">The placeholder name.</param>
    public static string? CollectionOf(string name)
    {
        int dot = name.IndexOf('.');
        return dot <= 0 ? null : name[..dot];
    }

    /// <summary>The part of a dotted placeholder after the dot.</summary>
    /// <param name="name">The placeholder name.</param>
    public static string MemberOf(string name)
    {
        int dot = name.IndexOf('.');
        return dot < 0 ? name : name[(dot + 1)..];
    }

    private static string? MergeFieldName(string instruction)
    {
        Match match = MergeFieldPattern().Match(instruction);
        return match.Success ? match.Groups[1].Value.Trim('"') : null;
    }

    [GeneratedRegex(@"\{\{\s*([^{}]+?)\s*\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex PlaceholderPattern();

    [GeneratedRegex(@"^MERGEFIELD\s+(""[^""]+""|\S+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MergeFieldPattern();
}
