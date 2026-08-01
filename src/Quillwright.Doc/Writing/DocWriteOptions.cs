using Quillwright.Diagnostics;

namespace Quillwright.Doc.Writing;

/// <summary>
/// Controls how a document is written to the legacy binary format, and where the losses are
/// reported.
/// </summary>
/// <remarks>
/// The binary format is older and narrower than the model, so some content cannot be written
/// as what it is. Rather than dropping it silently, every degradation raises a warning
/// naming what changed.
/// </remarks>
public sealed class DocWriteOptions
{
    /// <summary>Shared instance with default settings.</summary>
    public static DocWriteOptions Default { get; } = new();

    /// <summary>Invoked for every piece of content that could not be written as itself.</summary>
    public Action<DocumentWarning>? OnWarning { get; init; }

    /// <summary>
    /// Writes images into the document. Turning this off leaves a placeholder character
    /// where each picture was and keeps the file smaller. Default is <see langword="true"/>.
    /// </summary>
    public bool WriteImages { get; init; } = true;

    /// <summary>
    /// Writes hyperlinks as <c>HYPERLINK</c> fields, which is how the legacy format stores
    /// them. Turning this off writes only the display text. Default is <see langword="true"/>.
    /// </summary>
    public bool WriteHyperlinks { get; init; } = true;

    /// <summary>
    /// Locks the saved document behind a password ([MS-OFFCRYPTO] 2.3.5). The three content
    /// streams are encrypted with RC4, which is what the binary format has and is not strong;
    /// a caller who wants real encryption should be saving <c>.docx</c>.
    /// </summary>
    public string? Password { get; init; }
}
