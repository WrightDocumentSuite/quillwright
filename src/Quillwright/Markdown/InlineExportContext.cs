using Quillwright.Styles;

namespace Quillwright.Markdown;

/// <summary>
/// What <see cref="MarkdownInlineWalker"/> needs from the export it walks for. The walker
/// itself is format-neutral — it turns a paragraph into semantic tokens — and each exporter
/// that builds on it answers these questions its own way: Markdown distils formatting down to
/// what its syntax can say and reports the rest dropped, HTML keeps the resolved formatting
/// on the token and drops nothing.
/// </summary>
internal interface IInlineExportContext
{
    /// <summary>The formatting that actually applies, after the whole style chain.</summary>
    StyleResolver Resolver { get; }

    /// <summary>Which side of the tracked changes the export shows.</summary>
    MarkdownRevisionMode RevisionMode { get; }

    /// <summary>Whether hidden text is exported.</summary>
    bool IncludeHiddenText { get; }

    /// <summary>Whether pictures are exported.</summary>
    bool IncludePictures { get; }

    /// <summary>The ids the export gives to bookmarks.</summary>
    MarkdownAnchorRegistry Anchors { get; }

    /// <summary>
    /// The distilled inline style of a resolved format — and the place a format-poor target
    /// says what it had to drop. The resolved format itself travels on the token, so a
    /// format-rich target may ignore the distillation entirely.
    /// </summary>
    MarkdownInlineStyle DistillStyle(RunFormat resolved);

    /// <summary>Records one compromise the export had to make.</summary>
    void Report(MarkdownExportWarningKind kind, string message, string subject);
}
