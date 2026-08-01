using Quillwright.Diagnostics;

namespace Quillwright.Doc.Tests;

/// <summary>
/// A corpus of twenty-year-old files holds some no reader should accept: encrypted ones, and
/// ones written before Word 97. Refusing those is the right answer, so a measurement taken
/// over the corpus passes them by instead of failing on them.
/// </summary>
internal static class RefusedByDesign
{
    /// <summary>Whether the reader declined the file rather than failing to read it.</summary>
    public static bool Matches(Exception error) => error is DocFormatException or EncryptedDocumentException;
}
