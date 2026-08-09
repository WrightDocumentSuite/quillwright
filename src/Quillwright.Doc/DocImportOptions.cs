using Quillwright.Diagnostics;

namespace Quillwright.Doc;

/// <summary>Controls password handling and resource limits for a Word 97-2003 import.</summary>
public sealed record DocImportOptions
{
    /// <summary>The shared defaults.</summary>
    public static DocImportOptions Default { get; } = new();

    /// <summary>Password of an encrypted document, when it has one.</summary>
    public string? Password { get; init; }

    /// <summary>Limits for the compound file, its streams, media and embedded objects.</summary>
    public DocumentLoadBudget Budget { get; init; } = DocumentLoadBudget.Default;
}
