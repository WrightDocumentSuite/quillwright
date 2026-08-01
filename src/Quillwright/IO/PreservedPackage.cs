namespace Quillwright.IO;

/// <summary>
/// Everything a loaded package holds that the model does not regenerate: the raw bytes of
/// parts such as themes, charts, embedded objects and VBA, the relationships that point at
/// them, and the content-type declarations that make them openable.
/// </summary>
/// <remarks>
/// Saving is copy-on-write at the package level. Parts the model rebuilds are written from
/// the model; every other part is copied through byte for byte, with its relationships and
/// its content type. Relationship ids are kept as they were, because preserved markup refers
/// to its targets by id and renumbering would silently repoint a chart at a footnote.
/// </remarks>
internal sealed class PreservedPackage
{
    /// <summary>Raw content of parts carried over unchanged, keyed by absolute part name.</summary>
    public Dictionary<string, byte[]> Parts { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Relationships of each source part, keyed by the source part name.</summary>
    public Dictionary<string, List<OpcRelationship>> Relationships { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The content-type map of the loaded package.</summary>
    public ContentTypeMap ContentTypes { get; set; } = new();

    /// <summary>
    /// Parts the model owns that the loaded package actually had. A styles or numbering part
    /// that parsed to nothing still has to be written back, or the saved package would be
    /// missing a part the original declared.
    /// </summary>
    public HashSet<string> PresentModelledParts { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Absolute name of the main document part in the loaded package.</summary>
    public string MainPartPath { get; set; } = Formats.DocxSchema.PartDocument;

    /// <summary>Content type of the main document part, which tells a macro-enabled file from a plain one.</summary>
    public string MainContentType { get; set; } = Formats.DocxSchema.ContentTypeDocument;

    /// <summary>
    /// Where the parts the model owns actually live. A package is free to put styles.xml
    /// anywhere the relationship points, and Strict producers often do, so nothing may
    /// assume the conventional names.
    /// </summary>
    public Dictionary<string, string> ModelledPaths { get; } = new(StringComparer.Ordinal);

    /// <summary>Relationships of the main document part, in the order they were read.</summary>
    public List<OpcRelationship> MainRelationships =>
        Relationships.TryGetValue(MainPartPath, out List<OpcRelationship>? list) ? list : [];

    /// <summary>The part a relationship type resolves to, or the conventional name when there is none.</summary>
    public string PathFor(string relationshipType, string fallback)
    {
        if (ModelledPaths.TryGetValue(relationshipType, out string? cached))
            return cached;

        OpcRelationship relationship = MainRelationships.FirstOrDefault(r => r.Is(relationshipType));
        string resolved = relationship.Target is null || relationship.IsExternal
            ? fallback
            : OpcPath.Resolve(MainPartPath, relationship.Target);
        ModelledPaths[relationshipType] = resolved;
        return resolved;
    }

    /// <summary>
    /// Whether the package names its roles under <c>purl.oclc.org</c>. A Strict package is
    /// written back as Strict: converting only the parts the model regenerates would leave
    /// the copied ones speaking a different vocabulary in the same file.
    /// </summary>
    public bool IsStrict { get; set; }

    /// <summary>Resolves a relationship of the main document part by id.</summary>
    /// <param name="id">Relationship id used in the markup.</param>
    public OpcRelationship? FindMainRelationship(string? id) =>
        FindRelationship(MainPartPath, id);

    /// <summary>Resolves a relationship by id in the part that owns it.</summary>
    /// <param name="sourcePartPath">Absolute name of the source part.</param>
    /// <param name="id">Relationship id used by markup in that part.</param>
    public OpcRelationship? FindRelationship(string sourcePartPath, string? id)
    {
        if (id is null || !Relationships.TryGetValue(sourcePartPath, out List<OpcRelationship>? relationships))
            return null;

        return relationships.FirstOrDefault(r => r.Id == id) is { Id: not null } found ? found : null;
    }

    /// <summary>Absolute part name a relationship of the main document part points at.</summary>
    /// <param name="id">Relationship id used in the markup.</param>
    public string? ResolveMainTarget(string? id)
        => ResolveTarget(MainPartPath, id);

    /// <summary>Absolute part name a relationship owned by one source part points at.</summary>
    /// <param name="sourcePartPath">Absolute name of the source part.</param>
    /// <param name="id">Relationship id used by markup in that part.</param>
    public string? ResolveTarget(string sourcePartPath, string? id)
    {
        if (FindRelationship(sourcePartPath, id) is not { } relationship || relationship.IsExternal)
            return null;
        return OpcPath.Resolve(sourcePartPath, relationship.Target);
    }
}
