namespace Quillwright.Vba;

/// <summary>What the <c>dir</c> stream says about one module ([MS-OVBA] 2.3.4.2.3.2).</summary>
internal sealed class VbaModuleRecord
{
    /// <summary>Module name in the project's code page.</summary>
    public byte[] Name { get; init; } = [];

    /// <summary>Module name in UTF-16, when the stream carries one.</summary>
    public string? UnicodeName { get; set; }

    /// <summary>Name of the stream holding the module, in the project's code page.</summary>
    public byte[] StreamName { get; set; } = [];

    /// <summary>Name of the stream holding the module, in UTF-16.</summary>
    public string? UnicodeStreamName { get; set; }

    /// <summary>The module's description, in the project's code page.</summary>
    public byte[] Description { get; set; } = [];

    /// <summary>The module's description in UTF-16, when the stream carries one.</summary>
    public string? UnicodeDescription { get; set; }

    /// <summary>Where the source begins in the module's stream, past the opaque cache before it.</summary>
    public int TextOffset { get; set; } = -1;

    /// <summary>Whether the module is bound to a document, class or form rather than standing alone.</summary>
    public bool IsDocumentModule { get; set; }

    /// <summary>Whether the module is marked read-only.</summary>
    public bool IsReadOnly { get; set; }

    /// <summary>Whether the module is usable only from inside its own project.</summary>
    public bool IsPrivate { get; set; }
}
