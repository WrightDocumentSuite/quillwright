namespace Quillwright.Rtf;

/// <summary>An RTF stream is structurally invalid or exceeds a configured safety limit.</summary>
public sealed class RtfFormatException : Exception
{
    /// <summary>Creates an RTF format error.</summary>
    /// <param name="message">What is invalid.</param>
    /// <param name="byteOffset">Zero-based input offset where the problem was detected.</param>
    public RtfFormatException(string message, int byteOffset)
        : base($"{message} (byte {byteOffset}).") => ByteOffset = byteOffset;

    /// <summary>Zero-based input offset where the problem was detected.</summary>
    public int ByteOffset { get; }
}
