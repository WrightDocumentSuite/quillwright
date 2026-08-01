using Quillwright.Diagnostics;
using Quillwright.Model;

namespace Quillwright.Formats;

/// <summary>Reads a package into a document.</summary>
internal static partial class DocxLoader
{
    /// <summary>Reads the whole package and closes it, leaving no open handles behind.</summary>
    public static ValueTask<WordDocument> LoadAsync(Stream stream, LoadOptions options, CancellationToken cancellationToken) =>
        ReadAsync(stream, options, cancellationToken);
}
