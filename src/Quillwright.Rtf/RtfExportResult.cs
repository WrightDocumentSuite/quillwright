using System.Text;

namespace Quillwright.Rtf;

/// <summary>An encoded RTF file and every approximation made while producing it.</summary>
public sealed class RtfExportResult
{
    internal RtfExportResult(byte[] content, RtfExportDiagnostics diagnostics)
    {
        Content = content;
        Diagnostics = diagnostics;
    }

    /// <summary>The complete RTF byte stream.</summary>
    public ReadOnlyMemory<byte> Content { get; }

    /// <summary>Every loss or approximation made while exporting.</summary>
    public RtfExportDiagnostics Diagnostics { get; }

    /// <summary>Writes the RTF to a file, replacing it if it exists.</summary>
    /// <param name="path">Destination path.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public ValueTask SaveAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return new ValueTask(File.WriteAllBytesAsync(path, Content, cancellationToken));
    }

    /// <summary>Writes the RTF to a stream and leaves the stream open.</summary>
    /// <param name="stream">Destination stream.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public ValueTask SaveAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return stream.WriteAsync(Content, cancellationToken);
    }

    /// <inheritdoc />
    public override string ToString() => Encoding.ASCII.GetString(Content.Span);
}
