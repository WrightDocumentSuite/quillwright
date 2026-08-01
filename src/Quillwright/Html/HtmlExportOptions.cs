using System.Collections;
using System.Text;
using Quillwright.Markdown;

namespace Quillwright.Html;

/// <summary>Which side of the tracked changes an HTML export shows.</summary>
public enum HtmlRevisionMode : byte
{
    /// <summary>The document as it reads with every change accepted.</summary>
    Accepted = 0,

    /// <summary>The document as it read before the changes.</summary>
    Original,

    /// <summary>Both sides, insertions as <c>ins</c> and deletions as <c>del</c>.</summary>
    Marked,
}

/// <summary>How an HTML export carries its images.</summary>
public enum HtmlImageMode : byte
{
    /// <summary>Embedded in the page as <c>data:</c> URIs, so one file is the whole preview.</summary>
    DataUri = 0,

    /// <summary>Written beside the page and referenced by relative path, as the Markdown export does.</summary>
    Sidecar,
}

/// <summary>Controls how a document becomes HTML.</summary>
public sealed record HtmlExportOptions
{
    /// <summary>The options used when a caller passes none.</summary>
    public static HtmlExportOptions Default { get; } = new();

    /// <summary>Which side of the tracked changes the export shows.</summary>
    public HtmlRevisionMode RevisionMode { get; init; } = HtmlRevisionMode.Accepted;

    /// <summary>How images travel: inside the page, or beside it.</summary>
    public HtmlImageMode Images { get; init; } = HtmlImageMode.DataUri;

    /// <summary>Whether pictures are exported at all.</summary>
    public bool IncludePictures { get; init; } = true;

    /// <summary>Whether hidden text (<c>w:vanish</c>) is exported.</summary>
    public bool IncludeHiddenText { get; init; }

    /// <summary>
    /// Whether the output is a complete page — doctype, head, a small neutral stylesheet —
    /// rather than a body fragment for a host page of the caller's own.
    /// </summary>
    public bool FullDocument { get; init; } = true;

    /// <summary>The page title; the document's own title, and then nothing, when unset.</summary>
    public string? Title { get; init; }

    /// <summary>The page language, as a BCP 47 tag; the document's default when unset.</summary>
    public string? Language { get; init; }

    /// <summary>Directory name for sidecar images, relative to where the page is saved.</summary>
    public string MediaDirectoryName { get; init; } = "media";
}

/// <summary>What kind of compromise an HTML export made.</summary>
public enum HtmlExportWarningKind : byte
{
    /// <summary>Visual formatting HTML was not asked to carry was omitted.</summary>
    FormattingDropped = 0,

    /// <summary>Content the model cannot safely project was omitted.</summary>
    ContentSkipped,

    /// <summary>The content survived but its original structure was approximated.</summary>
    StructureApproximated,

    /// <summary>A potentially executable hyperlink target was not emitted.</summary>
    UnsafeLinkSkipped,

    /// <summary>An image was preserved, but browsers may not display its format.</summary>
    MediaMayNotRender,
}

/// <summary>One deliberate compromise made by an HTML export.</summary>
/// <param name="Kind">The kind of compromise.</param>
/// <param name="Message">A human-readable explanation.</param>
/// <param name="Subject">A stable feature, format, or source name involved.</param>
public readonly record struct HtmlExportWarning(
    HtmlExportWarningKind Kind,
    string Message,
    string? Subject = null)
{
    /// <inheritdoc />
    public override string ToString() => Subject is null ? Message : $"{Message} ({Subject})";
}

/// <summary>
/// The compromises an export had to make, in first-occurrence order. Repeated occurrences of
/// the same warning kind and subject are reported once.
/// </summary>
public sealed class HtmlExportDiagnostics : IReadOnlyList<HtmlExportWarning>
{
    private readonly List<HtmlExportWarning> _warnings = [];
    private readonly HashSet<(HtmlExportWarningKind Kind, string? Subject)> _seen = [];

    /// <inheritdoc />
    public int Count => _warnings.Count;

    /// <inheritdoc />
    public HtmlExportWarning this[int index] => _warnings[index];

    /// <summary>Whether the export needed no workaround.</summary>
    public bool IsEmpty => _warnings.Count == 0;

    /// <inheritdoc />
    public IEnumerator<HtmlExportWarning> GetEnumerator() => _warnings.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    public override string ToString() => string.Join(Environment.NewLine, _warnings);

    internal void Add(HtmlExportWarningKind kind, string message, string? subject = null)
    {
        if (_seen.Add((kind, subject)))
            _warnings.Add(new HtmlExportWarning(kind, message, subject));
    }
}

/// <summary>One image file referenced by a sidecar-mode HTML export.</summary>
public sealed class HtmlImage
{
    /// <summary>File name inside the media directory, for example <c>image1.png</c>.</summary>
    public required string FileName { get; init; }

    /// <summary>The encoded image bytes, passed through without re-encoding.</summary>
    public required ReadOnlyMemory<byte> Content { get; init; }

    /// <summary>The MIME type of <see cref="Content"/>.</summary>
    public required string ContentType { get; init; }

    /// <inheritdoc />
    public override string ToString() => $"{FileName} ({Content.Length} bytes)";
}

/// <summary>
/// An HTML page and the sidecar images it references. Rendering itself performs no I/O;
/// <see cref="SaveAsync"/> is the optional filesystem layer. In <see cref="HtmlImageMode.DataUri"/>
/// mode the page is self-contained and <see cref="Images"/> is empty.
/// </summary>
public sealed class HtmlDocument
{
    private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>The file name used by <see cref="SaveAsync"/>.</summary>
    public const string DefaultFileName = "document.html";

    internal HtmlDocument(
        string text,
        IReadOnlyList<HtmlImage> images,
        string mediaDirectoryName,
        HtmlExportDiagnostics diagnostics)
    {
        Text = text;
        Images = [.. images];
        MediaDirectoryName = mediaDirectoryName;
        Diagnostics = diagnostics;
    }

    /// <summary>The HTML, with LF line endings and exactly one trailing LF.</summary>
    public string Text { get; }

    /// <summary>Sidecar images in first-reference order; empty when images travel as data URIs.</summary>
    public IReadOnlyList<HtmlImage> Images { get; }

    /// <summary>The normalized relative directory used by image references.</summary>
    public string MediaDirectoryName { get; }

    /// <summary>Every deliberate approximation made while rendering.</summary>
    public HtmlExportDiagnostics Diagnostics { get; }

    /// <summary>
    /// Writes <see cref="DefaultFileName"/> and any sidecar images into a directory. Owned
    /// files are overwritten; unrelated files are left untouched.
    /// </summary>
    /// <param name="directoryPath">Directory that receives the export.</param>
    /// <param name="cancellationToken">Cancels writing.</param>
    public async ValueTask SaveAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(directoryPath);
        string root = Path.GetFullPath(directoryPath);
        Directory.CreateDirectory(root);

        await File.WriteAllTextAsync(
                Path.Combine(root, DefaultFileName), Text, Utf8WithoutBom, cancellationToken)
            .ConfigureAwait(false);

        if (Images.Count == 0)
            return;

        string media = MarkdownPath.ResolveInside(root, MediaDirectoryName);
        Directory.CreateDirectory(media);
        foreach (HtmlImage image in Images)
        {
            string path = MarkdownPath.ResolveInside(media, image.FileName);
            await File.WriteAllBytesAsync(path, image.Content, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public override string ToString() => Text;
}
