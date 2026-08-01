namespace Quillwright.IO;

/// <summary>
/// A single OPC relationship: how one package part points at another (or at an external resource).
/// </summary>
/// <param name="Id">Relationship identifier unique within the source part, e.g. <c>rId1</c>.</param>
/// <param name="Type">Relationship type URI exactly as it was read, so a Strict package keeps its own spelling.</param>
/// <param name="Target">Target part path or external URI.</param>
/// <param name="IsExternal">Whether the target lives outside the package.</param>
internal readonly record struct OpcRelationship(string Id, string Type, string Target, bool IsExternal = false)
{
    /// <summary>The Transitional spelling of <see cref="Type"/>, which every lookup uses.</summary>
    public string CanonicalType => Formats.DocxSchema.Canonical(Type);

    /// <summary>Returns <see langword="true"/> when this relationship plays the given role.</summary>
    public bool Is(string canonicalType) => CanonicalType == canonicalType;
}
