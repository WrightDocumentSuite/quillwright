namespace Quillwright.Markdown;

/// <summary>The Markdown dialect an export is allowed to use.</summary>
public enum MarkdownFlavor : byte
{
    /// <summary>
    /// GitHub Markdown: CommonMark, the GFM extensions, and GitHub's footnote syntax.
    /// </summary>
    GitHub = 0,

    /// <summary>
    /// CommonMark only. Constructs without CommonMark syntax use generated raw HTML.
    /// </summary>
    CommonMark,
}

/// <summary>Which side of tracked content changes is visible in the export.</summary>
public enum MarkdownRevisionMode : byte
{
    /// <summary>Show inserted/moved-to content and hide deleted/moved-from content.</summary>
    Accepted = 0,

    /// <summary>Show deleted/moved-from content and hide inserted/moved-to content.</summary>
    Original,

    /// <summary>
    /// Show both sides. Markdown has no marks to tell them apart, so they read as one text;
    /// the HTML export renders them as <c>ins</c> and <c>del</c>.
    /// </summary>
    Marked,
}

/// <summary>Controls how a Word document is projected into Markdown.</summary>
public sealed record MarkdownExportOptions
{
    internal static readonly MarkdownExportOptions Default = new();

    /// <summary>The Markdown dialect to emit.</summary>
    public MarkdownFlavor Flavor { get; init; } = MarkdownFlavor.GitHub;

    /// <summary>The tracked-content view to render without changing the source document.</summary>
    public MarkdownRevisionMode RevisionMode { get; init; } = MarkdownRevisionMode.Accepted;

    /// <summary>Whether pictures are referenced and returned as sidecar files.</summary>
    public bool IncludePictures { get; init; } = true;

    /// <summary>Whether runs marked as hidden are included.</summary>
    public bool IncludeHiddenText { get; init; }

    /// <summary>
    /// Whether a picture whose displayed size differs from its natural size is emitted as an
    /// HTML <c>img</c> element carrying width and height.
    /// </summary>
    public bool PreserveImageDimensions { get; init; } = true;

    /// <summary>
    /// Relative directory the generated image links point into. Rooted paths and parent
    /// traversal segments are rejected.
    /// </summary>
    public string MediaDirectoryName { get; init; } = "media";
}
