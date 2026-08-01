namespace Quillwright.Doc;

/// <summary>
/// Thrown when a legacy Word file cannot be read: it is not a compound file, it is encrypted,
/// or it predates the Word 97 format this reader understands.
/// </summary>
public sealed class DocFormatException : Exception
{
    /// <summary>Creates the exception with a message.</summary>
    public DocFormatException(string message) : base(message)
    {
    }

    /// <summary>Creates the exception with a message and the underlying failure.</summary>
    public DocFormatException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
