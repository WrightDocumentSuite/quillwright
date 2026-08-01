using System.Xml;
using Quillwright.Xml;

namespace Quillwright.IO;

/// <summary>
/// The <c>[Content_Types].xml</c> part: extension defaults plus per-part overrides.
/// </summary>
internal sealed class ContentTypeMap
{
    private readonly Dictionary<string, string> _defaults = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _overrides = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Registers a content type for a file extension (without the dot).</summary>
    public void AddDefault(string extension, string contentType) => _defaults[extension] = contentType;

    /// <summary>Registers a content type override for a specific part.</summary>
    public void AddOverride(string partPath, string contentType) => _overrides[partPath] = contentType;

    /// <summary>Drops the override of a part that is not going into the package.</summary>
    public void RemoveOverride(string partPath) => _overrides.Remove(partPath);

    /// <summary>Extension defaults, keyed by extension without the dot.</summary>
    public IReadOnlyDictionary<string, string> Defaults => _defaults;

    /// <summary>Per-part overrides, keyed by absolute part name.</summary>
    public IReadOnlyDictionary<string, string> Overrides => _overrides;

    /// <summary>Resolves the content type of a part, or <see langword="null"/> when unregistered.</summary>
    /// <remarks>
    /// An override names one part and beats the extension defaults; both are matched without
    /// regard to case (ECMA-376 part 2 §7.2.3.5).
    /// </remarks>
    public string? GetContentType(string partPath)
    {
        if (_overrides.TryGetValue(partPath, out string? overridden))
            return overridden;

        return Extension(partPath) is { } extension && _defaults.TryGetValue(extension, out string? byExtension)
            ? byExtension
            : null;
    }

    /// <summary>
    /// The extension of a part name: what follows the last dot <em>of the last segment</em>,
    /// or nothing when that segment has no dot.
    /// </summary>
    /// <remarks>
    /// Looking for the dot across the whole name would give <c>/word/media.v2/logo</c> an
    /// extension of <c>v2/logo</c> and match it against a default meant for something else.
    /// </remarks>
    private static string? Extension(string partPath)
    {
        int segment = partPath.LastIndexOf('/') + 1;
        int dot = partPath.LastIndexOf('.');
        return dot < segment ? null : partPath[(dot + 1)..];
    }

    /// <summary>Parses the content-types XML.</summary>
    public static ContentTypeMap Parse(Stream stream)
    {
        var map = new ContentTypeMap();
        using var reader = XmlReader.Create(stream, XmlDefaults.ReaderSettings);
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element)
                continue;

            if (reader.LocalName == "Default")
            {
                string? extension = reader.GetAttribute("Extension");
                string? contentType = reader.GetAttribute("ContentType");
                if (extension is not null && contentType is not null)
                    map.AddDefault(extension, contentType);
            }
            else if (reader.LocalName == "Override")
            {
                string? partName = reader.GetAttribute("PartName");
                string? contentType = reader.GetAttribute("ContentType");
                if (partName is not null && contentType is not null)
                    map.AddOverride(partName, contentType);
            }
        }

        return map;
    }

    /// <summary>Writes the content-types XML.</summary>
    public void Write(Utf8XmlWriter writer)
    {
        writer.WriteDeclaration();
        writer.WriteRaw("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"u8);
        foreach ((string extension, string contentType) in _defaults)
        {
            writer.WriteRaw("<Default Extension=\""u8);
            writer.WriteAttributeText(extension);
            writer.WriteRaw("\" ContentType=\""u8);
            writer.WriteAttributeText(contentType);
            writer.WriteRaw("\"/>"u8);
        }

        foreach ((string partPath, string contentType) in _overrides)
        {
            writer.WriteRaw("<Override PartName=\""u8);
            writer.WriteAttributeText(partPath);
            writer.WriteRaw("\" ContentType=\""u8);
            writer.WriteAttributeText(contentType);
            writer.WriteRaw("\"/>"u8);
        }

        writer.WriteRaw("</Types>"u8);
    }
}
