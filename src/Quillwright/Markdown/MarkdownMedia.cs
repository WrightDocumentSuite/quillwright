using System.Security.Cryptography;
using Quillwright.Model;

namespace Quillwright.Markdown;

/// <summary>Collects image files in first-reference order and deduplicates them by content.</summary>
internal sealed class MarkdownMedia
{
    private static readonly HashSet<string> BrowserFriendlyExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        "png", "jpg", "jpeg", "gif", "bmp", "svg", "webp",
    };

    private readonly string _directory;
    private readonly MarkdownExportDiagnostics _diagnostics;
    private readonly Dictionary<string, string> _namesByDigest = new(StringComparer.Ordinal);
    private readonly List<MarkdownImage> _images = [];

    public MarkdownMedia(string directory, MarkdownExportDiagnostics diagnostics)
    {
        _directory = directory;
        _diagnostics = diagnostics;
    }

    public IReadOnlyList<MarkdownImage> Images => _images;

    public string Add(ImageData image)
    {
        string digest = Convert.ToHexStringLower(SHA256.HashData(image.Bytes.Span));
        if (_namesByDigest.TryGetValue(digest, out string? existing))
            return existing;

        string extension = SafeExtension(image.Extension);
        string name = $"image{_images.Count + 1}.{extension}";
        _namesByDigest.Add(digest, name);
        _images.Add(new MarkdownImage
        {
            FileName = name,
            Content = image.Bytes,
            ContentType = image.ContentType,
        });

        if (!BrowserFriendlyExtensions.Contains(extension))
        {
            _diagnostics.Add(
                MarkdownExportWarningKind.MediaMayNotRender,
                $"The image was preserved as {extension}, but common Markdown viewers may not display it.",
                extension);
        }

        return name;
    }

    public string Reference(string fileName)
    {
        IEnumerable<string> segments = _directory.Split('/').Append(fileName);
        return string.Join('/', segments.Select(Uri.EscapeDataString));
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
}
