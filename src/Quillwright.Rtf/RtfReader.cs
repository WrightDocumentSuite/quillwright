using Quillwright.Diagnostics;
using Quillwright.Rtf.Parsing;

namespace Quillwright.Rtf;

/// <summary>Imports Rich Text Format (RTF 1.9.1) into the Quillwright document model.</summary>
public static class RtfReader
{
    /// <summary>Imports an RTF byte stream already held in memory.</summary>
    /// <param name="content">Complete RTF content, including its root group.</param>
    /// <param name="options">Resource limits and optional content.</param>
    public static RtfImportResult Load(ReadOnlySpan<byte> content, RtfImportOptions? options = null) =>
        new RtfParser(options ?? RtfImportOptions.Default).Parse(content);

    /// <summary>Imports an RTF file.</summary>
    /// <param name="path">Path to the RTF file.</param>
    /// <param name="options">Resource limits and optional content.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public static async ValueTask<RtfImportResult> LoadAsync(
        string path,
        RtfImportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        await using var stream = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.Open,
            Access = FileAccess.Read,
            Share = FileShare.Read,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
        });
        return await LoadAsync(stream, options, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Imports RTF from a stream and leaves the stream open.</summary>
    /// <param name="stream">Stream positioned at the beginning of the RTF.</param>
    /// <param name="options">Resource limits and optional content.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public static async ValueTask<RtfImportResult> LoadAsync(
        Stream stream,
        RtfImportOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        RtfImportOptions settings = options ?? RtfImportOptions.Default;
        settings.Validate();

        byte[] content;
        try
        {
            content = await DocumentInput.ReadBytesAsync(stream, settings.Budget, cancellationToken).ConfigureAwait(false);
        }
        catch (DocumentLoadLimitException exception)
            when (exception.LimitName == nameof(DocumentLoadBudget.MaxInputBytes))
        {
            throw new RtfFormatException(
                $"The input exceeds the {settings.MaxInputBytes}-byte limit",
                (int)Math.Min(exception.Observed, int.MaxValue));
        }

        return Load(content, settings);
    }
}
