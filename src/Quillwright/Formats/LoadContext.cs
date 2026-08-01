using Quillwright.Diagnostics;
using Quillwright.IO;
using Quillwright.Model;

namespace Quillwright.Formats;

/// <summary>
/// The state a part reader needs while a document is being loaded: where warnings go, which
/// part owns the relationship ids currently being read, and which images are already known.
/// </summary>
internal sealed class LoadContext
{
    /// <summary>Creates a context for a document being loaded.</summary>
    public LoadContext(WordDocument document, LoadOptions options, PreservedPackage preserved)
    {
        Document = document;
        Options = options;
        Preserved = preserved;
    }

    /// <summary>The document being built.</summary>
    public WordDocument Document { get; }

    /// <summary>How the caller asked for the document to be read.</summary>
    public LoadOptions Options { get; }

    /// <summary>What the package holds that the model does not regenerate.</summary>
    public PreservedPackage Preserved { get; }

    private readonly Dictionary<string, Dictionary<string, ImageData>> _imagesBySource =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Header and footer parts resolved from the main part's relationships.</summary>
    public Dictionary<string, HeaderFooter> HeadersByRelationship { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The part currently being read, used to attribute warnings.</summary>
    public string? CurrentPart { get; set; }

    /// <summary>The part whose markup is currently being interpreted.</summary>
    public string SourcePart => CurrentPart ?? Preserved.MainPartPath;

    private readonly Dictionary<Styles.RunFormat, Styles.RunFormat> _runFormats = [];
    private readonly Dictionary<Styles.ParagraphFormat, Styles.ParagraphFormat> _paragraphFormats = [];

    /// <summary>
    /// Returns the canonical instance of a format. A document with a hundred thousand runs
    /// normally has fewer than a hundred distinct formats, so interning turns the storage
    /// cost of formatting into one pointer per run.
    /// </summary>
    public Styles.RunFormat Intern(Styles.RunFormat format)
    {
        if (_runFormats.TryGetValue(format, out Styles.RunFormat? existing))
            return existing;
        _runFormats[format] = format;
        return format;
    }

    /// <inheritdoc cref="Intern(Styles.RunFormat)" />
    public Styles.ParagraphFormat Intern(Styles.ParagraphFormat format)
    {
        if (_paragraphFormats.TryGetValue(format, out Styles.ParagraphFormat? existing))
            return existing;
        _paragraphFormats[format] = format;
        return format;
    }

    /// <summary>Records a recoverable problem.</summary>
    public void Warn(WarningCode code, string message) =>
        Document.Warn(new DocumentWarning(code, message, CurrentPart), Options);

    /// <summary>Registers the image one relationship of a source part points at.</summary>
    public void RegisterImage(string sourcePart, string relationshipId, ImageData image)
    {
        if (!_imagesBySource.TryGetValue(sourcePart, out Dictionary<string, ImageData>? images))
            _imagesBySource[sourcePart] = images = new Dictionary<string, ImageData>(StringComparer.Ordinal);
        images[relationshipId] = image;
    }

    /// <summary>Resolves an image relationship in the part currently being read.</summary>
    public ImageData? ImageFor(string relationshipId) =>
        _imagesBySource.TryGetValue(SourcePart, out Dictionary<string, ImageData>? images) &&
        images.TryGetValue(relationshipId, out ImageData? image)
            ? image
            : null;

    /// <summary>Resolves the external target of a relationship of the current source part.</summary>
    public string? ExternalTarget(string? relationshipId) =>
        relationshipId is null ? null :
        Preserved.FindRelationship(SourcePart, relationshipId) is { IsExternal: true } relationship ? relationship.Target : null;

    /// <summary>Resolves a relationship of the current source part to the part it names.</summary>
    /// <param name="relationshipId">The relationship id the markup used.</param>
    public string? PartFor(string? relationshipId)
    {
        if (relationshipId is null)
            return null;

        return Preserved.FindRelationship(SourcePart, relationshipId) is { IsExternal: false, Target: { } target }
            ? OpcPath.Resolve(SourcePart, target)
            : null;
    }
}
