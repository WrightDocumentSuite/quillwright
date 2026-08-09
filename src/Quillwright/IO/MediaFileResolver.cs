using System.Buffers;

namespace Quillwright.IO;

/// <summary>The outcome of reading one importer-owned local media reference.</summary>
internal enum MediaFileReadStatus : byte
{
    Success = 0,
    Missing,
    Unsafe,
    Unreadable,
    TooLarge,
}

/// <summary>A local media read and, when it succeeded, the bytes it read.</summary>
internal readonly record struct MediaFileReadResult(MediaFileReadStatus Status, byte[]? Bytes, long Length = 0);

/// <summary>
/// Reads a relative media reference without letting it leave the caller's trusted directory.
/// </summary>
internal static class MediaFileResolver
{
    private static readonly SearchValues<char> PortableInvalidFileNameChars =
        SearchValues.Create(['\0', '<', '>', ':', '"', '|', '?', '*', '#']);

    /// <summary>
    /// Resolves and reads one existing file below <paramref name="mediaDirectory"/>. The trusted
    /// directory itself may be a link chosen by the caller; links below it are never followed.
    /// </summary>
    public static MediaFileReadResult Read(string mediaDirectory, string reference) =>
        Read(mediaDirectory, reference, Array.MaxLength);

    /// <summary>Resolves and reads one file only when its length is within the supplied ceiling.</summary>
    public static MediaFileReadResult Read(
        string mediaDirectory, string reference, long maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(mediaDirectory);
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBytes, 1);

        try
        {
            if (string.IsNullOrWhiteSpace(mediaDirectory) || !HasValidPercentEncoding(reference))
                return new(MediaFileReadStatus.Unsafe, null);

            string decoded = Uri.UnescapeDataString(reference);
            if (!TryRelativePath(decoded, out string relative))
                return new(MediaFileReadStatus.Unsafe, null);

            string root = Path.GetFullPath(mediaDirectory);
            string candidate = Path.GetFullPath(Path.Combine(root, relative));
            if (!IsInside(root, candidate))
                return new(MediaFileReadStatus.Unsafe, null);

            if (!Directory.Exists(root))
                return new(MediaFileReadStatus.Missing, null);

            // Walk before any existence probe for the candidate: File.Exists itself may follow
            // an intermediate junction or symbolic link, including one whose target is remote.
            if (HasReparsePoint(root, candidate))
                return new(MediaFileReadStatus.Unsafe, null);

            // Holding a non-delete-sharing handle narrows the check/open race on Windows. Check
            // the path again after opening so a link inserted between the first check and open
            // is not knowingly followed on any platform.
            using var stream = new FileStream(
                candidate,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.SequentialScan);

            if (HasReparsePoint(root, candidate))
                return new(MediaFileReadStatus.Unsafe, null);

            if (stream.Length > maximumBytes || stream.Length > Array.MaxLength)
                return new(MediaFileReadStatus.TooLarge, null, stream.Length);

            var contents = new byte[(int)stream.Length];
            stream.ReadExactly(contents);
            return new(MediaFileReadStatus.Success, contents);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException)
        {
            return new(MediaFileReadStatus.Missing, null);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or UriFormatException)
        {
            return new(MediaFileReadStatus.Unsafe, null);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new(MediaFileReadStatus.Unreadable, null);
        }
    }

    /// <summary>
    /// Makes a portable relative path. Parent and current-directory segments are rejected rather
    /// than normalised because their meaning can change while walking through a symbolic link.
    /// </summary>
    private static bool TryRelativePath(string reference, out string relative)
    {
        relative = string.Empty;
        if (reference.Length == 0)
            return false;

        string portable = reference.Replace('\\', '/');
        if (portable.StartsWith('/') || Path.IsPathRooted(reference) || Path.IsPathRooted(portable) ||
            (portable.Length >= 2 && char.IsAsciiLetter(portable[0]) && portable[1] == ':'))
        {
            return false;
        }

        string[] segments = portable.Split('/');
        if (segments.Any(static segment => segment.Length == 0 || segment is "." or ".." ||
                                           segment.AsSpan().IndexOfAny(PortableInvalidFileNameChars) >= 0 ||
                                           segment.Any(char.IsControl) ||
                                           segment.EndsWith(' ') || segment.EndsWith('.') ||
                                           IsWindowsDeviceName(segment)))
        {
            return false;
        }

        relative = Path.Combine(segments);
        return true;
    }

    /// <summary>Rejects malformed escapes before the one and only percent-decoding pass.</summary>
    private static bool HasValidPercentEncoding(string reference)
    {
        for (int i = 0; i < reference.Length; i++)
        {
            if (reference[i] != '%')
                continue;

            if (i + 2 >= reference.Length || !char.IsAsciiHexDigit(reference[i + 1]) ||
                !char.IsAsciiHexDigit(reference[i + 2]))
            {
                return false;
            }

            i += 2;
        }

        return true;
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

    /// <summary>Whether a fully-qualified candidate is a proper descendant of the root.</summary>
    private static bool IsInside(string root, string candidate)
    {
        string prefix = Path.EndsInDirectorySeparator(root)
            ? root
            : root + Path.DirectorySeparatorChar;

        return candidate.StartsWith(prefix, PathComparison);
    }

    /// <summary>Checks every existing component below the trusted root, including the file.</summary>
    private static bool HasReparsePoint(string root, string candidate)
    {
        string relative = Path.GetRelativePath(root, candidate);
        string current = root;
        foreach (string segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                return true;
        }

        return false;
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;
}
