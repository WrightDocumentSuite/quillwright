using System.Security.Cryptography;
using Quillwright.Markdown;
using Quillwright.Model;
using Quillwright.Rendering;
using Quillwright.Styles;

namespace Quillwright.Html;

/// <summary>State shared by the writers of one HTML export.</summary>
/// <remarks>
/// Implements the walker's <see cref="IInlineExportContext"/> the format-rich way: nothing is
/// distilled away and nothing is reported dropped, because the resolved formatting travels on
/// the token and the HTML writer says all of it in CSS.
/// </remarks>
internal sealed class HtmlContext : IInlineExportContext
{
    private static readonly HashSet<string> BrowserFriendlyExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "png", "jpg", "jpeg", "gif", "bmp", "svg", "webp",
    };

    private readonly Dictionary<string, string> _sidecarByDigest = new(StringComparer.Ordinal);
    private readonly List<HtmlImage> _images = [];
    private readonly Dictionary<(bool Endnote, int Id), HtmlNoteEntry> _notesByReference = [];
    private readonly List<HtmlNoteEntry> _notes = [];

    public HtmlContext(WordDocument document, HtmlExportOptions options, string mediaDirectoryName, HtmlExportDiagnostics diagnostics)
    {
        Document = document;
        Options = options;
        MediaDirectoryName = mediaDirectoryName;
        Diagnostics = diagnostics;
        Anchors = new MarkdownAnchorRegistry();
        Numbering = new NumberingResolver(document.Numbering);
        Lists = new NumberingCounter(Numbering);
        RegisterBookmarks(document.Blocks);
    }

    public WordDocument Document { get; }

    public HtmlExportOptions Options { get; }

    public string MediaDirectoryName { get; }

    public HtmlExportDiagnostics Diagnostics { get; }

    public MarkdownAnchorRegistry Anchors { get; }

    public NumberingResolver Numbering { get; }

    public NumberingCounter Lists { get; }

    public IReadOnlyList<HtmlImage> Images => _images;

    public IReadOnlyList<HtmlNoteEntry> Notes => _notes;

    public StyleResolver Resolver => Document.Resolver;

    public ParagraphFormat ResolveParagraphFormat(Paragraph paragraph) =>
        Resolver.ResolveParagraphFormat(paragraph, Numbering);

    public MarkdownRevisionMode RevisionMode => Options.RevisionMode switch
    {
        HtmlRevisionMode.Original => MarkdownRevisionMode.Original,
        HtmlRevisionMode.Marked => MarkdownRevisionMode.Marked,
        _ => MarkdownRevisionMode.Accepted,
    };

    public bool IncludeHiddenText => Options.IncludeHiddenText;

    public bool IncludePictures => Options.IncludePictures;

    /// <summary>HTML keeps the resolved formatting whole, so nothing is distilled or dropped.</summary>
    public MarkdownInlineStyle DistillStyle(RunFormat resolved) => new(
        resolved.Bold == true,
        resolved.Italic == true,
        resolved.Strike == true || resolved.DoubleStrike == true,
        resolved.Underline is { } underline && underline != UnderlineStyle.None,
        MarkdownInlineWalker.IsMonospace(resolved),
        resolved.VerticalAlignment ?? VerticalTextAlignment.Baseline);

    public void Report(MarkdownExportWarningKind kind, string message, string subject)
    {
        HtmlExportWarningKind mapped = kind switch
        {
            MarkdownExportWarningKind.FormattingDropped => HtmlExportWarningKind.FormattingDropped,
            MarkdownExportWarningKind.ContentSkipped => HtmlExportWarningKind.ContentSkipped,
            MarkdownExportWarningKind.UnsafeLinkSkipped => HtmlExportWarningKind.UnsafeLinkSkipped,
            MarkdownExportWarningKind.MediaMayNotRender => HtmlExportWarningKind.MediaMayNotRender,
            _ => HtmlExportWarningKind.StructureApproximated,
        };

        Diagnostics.Add(mapped, message.Replace("Markdown", "HTML", StringComparison.Ordinal), subject);
    }

    /// <summary>The <c>src</c> for an image: a data URI, or a sidecar file reference.</summary>
    public string ImageSource(ImageData image)
    {
        if (Options.Images == HtmlImageMode.DataUri)
            return $"data:{image.ContentType};base64,{Convert.ToBase64String(image.Bytes.Span)}";

        string digest = Convert.ToHexStringLower(SHA256.HashData(image.Bytes.Span));
        if (!_sidecarByDigest.TryGetValue(digest, out string? name))
        {
            string extension = SafeExtension(image.Extension);
            name = $"image{_images.Count + 1}.{extension}";
            _sidecarByDigest.Add(digest, name);
            _images.Add(new HtmlImage { FileName = name, Content = image.Bytes, ContentType = image.ContentType });

            if (!BrowserFriendlyExtensions.Contains(extension))
            {
                Diagnostics.Add(
                    HtmlExportWarningKind.MediaMayNotRender,
                    $"The image was preserved as {extension}, but browsers may not display it.",
                    extension);
            }
        }

        IEnumerable<string> segments = MediaDirectoryName.Split('/').Append(name);
        return string.Join('/', segments.Select(Uri.EscapeDataString));
    }

    /// <summary>The note a reference points at, numbered in first-reference order.</summary>
    public HtmlNoteEntry? Note(NoteReference reference)
    {
        var key = (reference.IsEndnote, reference.Id);
        if (_notesByReference.TryGetValue(key, out HtmlNoteEntry? existing))
            return existing;

        IReadOnlyList<Note> notes = reference.IsEndnote ? Document.Endnotes : Document.Footnotes;
        Note? body = notes.FirstOrDefault(note => note.Kind == NoteKind.Normal && note.Id == reference.Id);
        if (body is null)
        {
            Diagnostics.Add(
                HtmlExportWarningKind.ContentSkipped,
                "A note reference points to a note body that is not present.",
                reference.IsEndnote ? "missing-endnote" : "missing-footnote");
            return null;
        }

        var entry = new HtmlNoteEntry
        {
            Number = _notes.Count + 1,
            Label = $"{(reference.IsEndnote ? "en" : "fn")}-{Math.Abs((long)reference.Id)}-{_notes.Count + 1}",
            Body = body,
        };

        _notesByReference.Add(key, entry);
        _notes.Add(entry);
        return entry;
    }

    private static string SafeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return "bin";

        string lowered = extension.TrimStart('.').ToLowerInvariant();
        return lowered.Length is > 0 and <= 16 && lowered.All(char.IsAsciiLetterOrDigit)
            ? lowered
            : "bin";
    }

    private void RegisterBookmarks(IEnumerable<Block> blocks)
    {
        foreach (Block block in blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    foreach ((int _, InlineMark mark) in paragraph.Marks)
                    {
                        if (mark is BookmarkStart bookmark)
                            Anchors.Register(bookmark);
                    }

                    break;

                case Table table:
                    foreach (TableRow row in table.Rows)
                    {
                        foreach (TableCell cell in row.Cells)
                            RegisterBookmarks(cell.Blocks);
                    }

                    break;

                case BlockContentControl control:
                    RegisterBookmarks(control.Blocks);
                    break;

                case AlternateContentBlock alternate:
                    RegisterBookmarks(alternate.Blocks);
                    break;

                default:
                    break;
            }
        }
    }
}

/// <summary>A referenced footnote or endnote waiting for its definition at the foot of the page.</summary>
internal sealed class HtmlNoteEntry
{
    private readonly List<string> _referenceLabels = [];

    public required int Number { get; init; }

    public required string Label { get; init; }

    public required Note Body { get; init; }

    public IReadOnlyList<string> ReferenceLabels => _referenceLabels;

    public string AddReference()
    {
        string label = _referenceLabels.Count == 0
            ? Label + "-ref"
            : $"{Label}-ref-{_referenceLabels.Count + 1}";
        _referenceLabels.Add(label);
        return label;
    }
}
