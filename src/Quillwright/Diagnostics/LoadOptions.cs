namespace Quillwright.Diagnostics;

/// <summary>
/// Controls how a document is read. The defaults favour fidelity: everything the model does
/// not understand is kept so that saving does not destroy it.
/// </summary>
public sealed class LoadOptions
{
    /// <summary>Shared instance with default settings.</summary>
    public static LoadOptions Default { get; } = new();

    /// <summary>
    /// Resource limits applied while opening the package and parsing its XML. The defaults
    /// accept ordinary large documents while bounding ZIP expansion and markup complexity.
    /// </summary>
    public DocumentLoadBudget Budget { get; init; } = DocumentLoadBudget.Default;

    /// <summary>
    /// Invoked for every recoverable problem as it is found. Warnings are also collected in
    /// <see cref="Model.WordDocument.LoadDiagnostics"/>.
    /// </summary>
    public Action<DocumentWarning>? OnWarning { get; init; }

    /// <summary>
    /// Has no effect: package parts and markup the model does not represent are always kept, so
    /// macros, embedded objects and custom XML survive a save whatever this says. To remove them,
    /// use <c>SaveSanitizedAsync</c> from <c>Quillwright.Sanitization</c>.
    /// </summary>
    [Obsolete("This option has no effect. Package parts the model does not represent are always preserved, so it cannot be used to remove macros, embedded objects or custom XML. Use SaveSanitizedAsync to produce a clean copy.")]
    public bool PreserveUnknownContent { get; init; } = true;

    /// <summary>
    /// Reads the bytes of images and other media into the model. When off, media parts are
    /// still carried over to the saved package but their content is not exposed. Default is
    /// <see langword="true"/>.
    /// </summary>
    public bool LoadMedia { get; init; } = true;

    /// <summary>
    /// The password of an encrypted document. Without one an encrypted document is refused
    /// with an <see cref="EncryptedDocumentException"/> rather than opened.
    /// </summary>
    /// <remarks>
    /// Reading is all this does. A document opened with a password is saved unencrypted,
    /// because this library decrypts but does not encrypt.
    /// </remarks>
    public string? Password { get; init; }
}
