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
    /// Invoked for every recoverable problem as it is found. Warnings are also collected in
    /// <see cref="Model.WordDocument.LoadDiagnostics"/>.
    /// </summary>
    public Action<DocumentWarning>? OnWarning { get; init; }

    /// <summary>
    /// Keeps package parts and markup the model does not represent so that saving restores
    /// them. Turning this off produces smaller documents and loses charts, embedded objects,
    /// VBA and custom XML. Default is <see langword="true"/>.
    /// </summary>
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
