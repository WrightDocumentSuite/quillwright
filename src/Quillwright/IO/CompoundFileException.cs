namespace Quillwright.IO;

/// <summary>Thrown when bytes that should be a compound file are not one.</summary>
public sealed class CompoundFileException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What is wrong with the bytes.</param>
    public CompoundFileException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception.</summary>
    public CompoundFileException()
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">What is wrong with the bytes.</param>
    /// <param name="innerException">The underlying failure.</param>
    public CompoundFileException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
