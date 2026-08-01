using System.Text.RegularExpressions;
using Quillwright.Model;
using Quillwright.Styles;

namespace Quillwright.Editing;

/// <summary>One match of a search, as an offset range inside a paragraph.</summary>
/// <param name="Paragraph">The paragraph the match is in.</param>
/// <param name="Start">Offset of the first matched character.</param>
/// <param name="Length">Number of matched characters.</param>
/// <param name="Value">The matched text.</param>
public readonly record struct TextMatch(Paragraph Paragraph, int Start, int Length, string Value);

/// <summary>How a search treats the text it looks through.</summary>
public sealed class SearchOptions
{
    /// <summary>Shared instance with default settings.</summary>
    public static SearchOptions Default { get; } = new();

    /// <summary>Whether case matters. Default is <see langword="true"/>.</summary>
    public bool MatchCase { get; init; } = true;

    /// <summary>Whether the pattern is a regular expression rather than literal text.</summary>
    public bool IsRegex { get; init; }

    /// <summary>Whether only whole words count as a match.</summary>
    public bool WholeWord { get; init; }

    /// <summary>Whether headers, footers, notes and comments are searched as well as the body.</summary>
    public bool IncludeSecondaryStories { get; init; } = true;
}

/// <summary>
/// Finding and replacing text across a document.
/// </summary>
/// <remarks>
/// Word splits a sentence into runs at every formatting change and at many edits besides, so
/// a phrase a reader sees as one string is usually several runs in the file. Because a
/// paragraph here keeps its text in one buffer with formatting laid over it as ranges,
/// searching needs no stitching and a replacement that spans a run boundary is an ordinary
/// splice.
/// </remarks>
public static class TextSearch
{
    /// <summary>Finds every occurrence of a pattern in the document.</summary>
    /// <param name="document">Document to search.</param>
    /// <param name="pattern">Text or regular expression to look for.</param>
    /// <param name="options">How the search treats the text.</param>
    public static IEnumerable<TextMatch> Find(this WordDocument document, string pattern, SearchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrEmpty(pattern);
        options ??= SearchOptions.Default;

        Regex regex = BuildRegex(pattern, options);
        foreach (Paragraph paragraph in Paragraphs(document, options))
        {
            foreach (TextMatch match in Find(paragraph, regex))
                yield return match;
        }
    }

    /// <summary>Finds every occurrence of a pattern in one paragraph.</summary>
    /// <param name="paragraph">Paragraph to search.</param>
    /// <param name="pattern">Text or regular expression to look for.</param>
    /// <param name="options">How the search treats the text.</param>
    public static IEnumerable<TextMatch> Find(this Paragraph paragraph, string pattern, SearchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        return Find(paragraph, BuildRegex(pattern, options ?? SearchOptions.Default));
    }

    /// <summary>
    /// Replaces every occurrence of a pattern. The replacement takes the formatting of the
    /// text it stands in for, and any hyperlink or content control that covered the whole
    /// match keeps covering the replacement.
    /// </summary>
    /// <param name="document">Document to change.</param>
    /// <param name="pattern">Text or regular expression to look for.</param>
    /// <param name="replacement">Text to put in its place; supports <c>$1</c> groups when the pattern is a regular expression.</param>
    /// <param name="options">How the search treats the text.</param>
    /// <returns>How many occurrences were replaced.</returns>
    public static int Replace(this WordDocument document, string pattern, string replacement, SearchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(replacement);
        options ??= SearchOptions.Default;

        Regex regex = BuildRegex(pattern, options);
        int count = 0;
        foreach (Paragraph paragraph in Paragraphs(document, options).ToArray())
            count += Replace(paragraph, regex, replacement);

        return count;
    }

    /// <summary>Replaces every occurrence of a pattern in one paragraph.</summary>
    /// <param name="paragraph">Paragraph to change.</param>
    /// <param name="pattern">Text or regular expression to look for.</param>
    /// <param name="replacement">Text to put in its place.</param>
    /// <param name="options">How the search treats the text.</param>
    public static int Replace(this Paragraph paragraph, string pattern, string replacement, SearchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(paragraph);
        return Replace(paragraph, BuildRegex(pattern, options ?? SearchOptions.Default), replacement);
    }

    /// <summary>Applies a formatting change to every occurrence of a pattern.</summary>
    /// <param name="document">Document to change.</param>
    /// <param name="pattern">Text or regular expression to look for.</param>
    /// <param name="transform">Produces the new formatting from the old.</param>
    /// <param name="options">How the search treats the text.</param>
    public static int Highlight(this WordDocument document, string pattern, Func<RunFormat, RunFormat> transform, SearchOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(transform);

        int count = 0;
        foreach (TextMatch match in Find(document, pattern, options).ToArray())
        {
            match.Paragraph.ApplyFormat(match.Start, match.Length, transform);
            count++;
        }

        return count;
    }

    private static IEnumerable<TextMatch> Find(Paragraph paragraph, Regex regex)
    {
        foreach (Match match in regex.Matches(paragraph.Text).Cast<Match>())
            yield return new TextMatch(paragraph, match.Index, match.Length, match.Value);
    }

    private static int Replace(Paragraph paragraph, Regex regex, string replacement)
    {
        // Later matches are replaced first so the offsets of the earlier ones stay valid.
        Match[] matches = [.. regex.Matches(paragraph.Text).Cast<Match>()];
        for (int i = matches.Length - 1; i >= 0; i--)
        {
            Match match = matches[i];
            paragraph.ReplaceText(match.Index, match.Length, match.Result(replacement));
        }

        return matches.Length;
    }

    private static IEnumerable<Paragraph> Paragraphs(WordDocument document, SearchOptions options) =>
        options.IncludeSecondaryStories
            ? document.AllContainers.SelectMany(static container => container.Blocks.Paragraphs)
            : document.Paragraphs;

    private static Regex BuildRegex(string pattern, SearchOptions options)
    {
        string expression = options.IsRegex ? pattern : Regex.Escape(pattern);
        if (options.WholeWord)
            expression = $@"\b(?:{expression})\b";

        RegexOptions flags = RegexOptions.CultureInvariant;
        if (!options.MatchCase)
            flags |= RegexOptions.IgnoreCase;
        return new Regex(expression, flags);
    }
}
