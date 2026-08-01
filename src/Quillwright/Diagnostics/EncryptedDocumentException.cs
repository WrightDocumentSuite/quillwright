namespace Quillwright.Diagnostics;

/// <summary>
/// Thrown when a document is encrypted. This library reads Word files; it does not decrypt
/// them, and saying so is more use than reporting the container as damaged.
/// </summary>
/// <remarks>
/// An encrypted OOXML document is not a zip at all: the package is stored, encrypted, as a
/// stream inside a compound file next to the key material that unlocks it ([MS-OFFCRYPTO]
/// 2.3.4.4 and 2.3.4.5). A legacy <c>.doc</c> instead records encryption in a flag of its
/// header ([MS-DOC] 2.5.1). Both refusals arrive here, so one <c>catch</c> covers either
/// format.
/// </remarks>
public class EncryptedDocumentException : DocxFormatException
{
    /// <summary>Creates the exception with a message.</summary>
    public EncryptedDocumentException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with a message and the underlying failure.</summary>
    public EncryptedDocumentException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
