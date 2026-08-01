namespace Quillwright.Diagnostics;

/// <summary>
/// Thrown when a package is broken beyond recovery: not a zip, no main document part, or
/// XML that no longer parses. Problems a reader can work around become a
/// <see cref="DocumentWarning"/> instead of an exception.
/// </summary>
/// <remarks>
/// A file that is intact but unreadable for a reason worth acting on refines this:
/// <see cref="EncryptedDocumentException"/> says the document is encrypted rather than
/// damaged.
/// </remarks>
public class DocxFormatException : Exception
{
    /// <summary>Creates the exception with a message.</summary>
    public DocxFormatException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with a message and the underlying failure.</summary>
    public DocxFormatException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
