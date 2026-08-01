using System.Text;

namespace Quillwright.Markdown;

/// <summary>One image file referenced by a Markdown export.</summary>
public sealed class MarkdownImage
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
/// A Markdown document and the sidecar images it references. Rendering itself performs no I/O;
/// <see cref="SaveAsync"/> is the optional filesystem layer.
/// </summary>
public sealed class MarkdownDocument
{
    private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>The file name used by <see cref="SaveAsync"/>.</summary>
    public const string DefaultFileName = "document.md";

    internal MarkdownDocument(
        string text,
        IReadOnlyList<MarkdownImage> images,
        string mediaDirectoryName,
        MarkdownExportDiagnostics diagnostics)
    {
        Text = text;
        Images = [.. images];
        MediaDirectoryName = mediaDirectoryName;
        Diagnostics = diagnostics;
    }

    /// <summary>The Markdown text, with LF line endings and exactly one trailing LF.</summary>
    public string Text { get; }

    /// <summary>Images in first-reference order.</summary>
    public IReadOnlyList<MarkdownImage> Images { get; }

    /// <summary>The normalized relative directory used by image links.</summary>
    public string MediaDirectoryName { get; }

    /// <summary>Every deliberate approximation made while rendering.</summary>
    public MarkdownExportDiagnostics Diagnostics { get; }

    /// <summary>
    /// Writes <see cref="DefaultFileName"/> and referenced images into a directory. Owned files
    /// are overwritten; unrelated and stale files are left untouched. Cancellation is not
    /// transactional and may leave files already written.
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
        foreach (MarkdownImage image in Images)
        {
            string path = MarkdownPath.ResolveInside(media, image.FileName);
            await File.WriteAllBytesAsync(path, image.Content, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public override string ToString() => Text;
}

internal static class MarkdownPath
{
    private static readonly char[] PortableInvalidFileNameChars =
        ['\0', '<', '>', ':', '"', '|', '?', '*'];

    public static string NormalizeMediaDirectoryName(string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string path = value.Replace('\\', '/');

        if (Path.IsPathRooted(value) || path.StartsWith('/') || path.EndsWith('/') ||
            (path.Length >= 2 && char.IsAsciiLetter(path[0]) && path[1] == ':'))
        {
            throw new ArgumentException("The Markdown media directory must be a relative path.", nameof(value));
        }

        string[] segments = path.Split('/');
        foreach (string segment in segments)
        {
            if (segment.Length == 0 || segment is "." or ".." ||
                segment.IndexOfAny(PortableInvalidFileNameChars) >= 0 ||
                segment.Any(char.IsControl) || segment.EndsWith(' ') || segment.EndsWith('.') ||
                IsWindowsDeviceName(segment))
            {
                throw new ArgumentException(
                    "The Markdown media directory contains an unsafe path segment.", nameof(value));
            }
        }

        return string.Join('/', segments);
    }

    private static bool IsWindowsDeviceName(string segment)
    {
        string stem = segment.Split('.')[0];
        if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("NUL", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return stem.Length == 4 && stem[3] is >= '1' and <= '9' &&
               (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase));
    }

    public static string ResolveInside(string root, string relative)
    {
        string fullRoot = Path.GetFullPath(root);
        string platformRelative = relative.Replace('/', Path.DirectorySeparatorChar);
        string candidate = Path.GetFullPath(Path.Combine(fullRoot, platformRelative));
        string prefix = Path.EndsInDirectorySeparator(fullRoot)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;

        if (!candidate.StartsWith(prefix, PathComparison))
            throw new ArgumentException("The Markdown export path leaves its destination directory.", nameof(relative));

        return candidate;
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}
