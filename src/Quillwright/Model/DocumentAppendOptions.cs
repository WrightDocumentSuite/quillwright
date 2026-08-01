namespace Quillwright.Model;

/// <summary>How appended content arrives in the target document.</summary>
public sealed record DocumentAppendOptions
{
    /// <summary>The options used when a caller passes none.</summary>
    public static DocumentAppendOptions Default { get; } = new();

    /// <summary>
    /// Whether the source's sections arrive as sections of their own, keeping their page
    /// setup, headers and footers. When off — the default — the content flows into the
    /// target's last section and wears its page setup.
    /// </summary>
    public bool KeepSections { get; init; }

    /// <summary>
    /// Whether the appended content starts on a new page. Meaningful only when the content
    /// flows into the target's last section; a kept section brings its own start.
    /// </summary>
    public bool StartOnNewPage { get; init; }
}
